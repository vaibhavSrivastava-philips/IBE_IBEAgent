using System.Text.Json;
using System.Text.Json.Serialization;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Security;
using Philips.IBE.IBEAgent.Telemetry;

namespace Philips.IBE.IBEAgent.Persistence;

// §3.9 — production-safe local durable forward store. Each failed delivery is one encrypted JSON row
// on disk, leased during FetchDueAsync so concurrent workers do not replay the same row. Writes are
// atomic (temp file + move) and replay rows survive process restart.
public sealed class FileForwardStore : IForwardStore, IForwardStoreManagement
{
    private sealed record Row(
        long Id,
        Guid MessageId,
        byte[] EncryptedMessage,
        int OutputId,
        ForwardStatus Status,
        int Attempts,
        DateTimeOffset NextAttemptAt,
        DateTimeOffset? LeasedUntil,
        string? LastError,
        DateTimeOffset CreatedAt);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _directory;
    private readonly IDataProtector _protector;
    private readonly TimeSpan _leaseDuration;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileForwardStore(string directory, IDataProtector protector, TimeSpan leaseDuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _leaseDuration = leaseDuration;
        Directory.CreateDirectory(_directory);
    }

    public async Task StoreAsync(MessageContext context, int outputId, string? error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var envelope = new ReplayEnvelope
        {
            MessageId = context.MessageId,
            CorrelationId = context.CorrelationId,
            SourceEndpointId = context.SourceEndpointId,
            Format = context.Format,
            Headers = new Dictionary<string, string>(context.Headers, StringComparer.Ordinal),
            Payload = context.Payload.ToArray(),
        };

        var now = DateTimeOffset.UtcNow;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var id = NextIdUnsafe(now);
            var row = new Row(
                id,
                envelope.MessageId,
                _protector.Protect(envelope.ToPlaintext()),
                outputId,
                ForwardStatus.Pending,
                0,
                now,
                null,
                error,
                now);

            await WriteRowAsync(row, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        AgentDiagnostics.ForwardStored.Add(1, new KeyValuePair<string, object?>("outputId", outputId));
    }

    public async Task ResolveAsync(MessageContext context, int outputId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var path in RowFiles())
            {
                var row = await ReadRowAsync(path, cancellationToken).ConfigureAwait(false);
                if (row is not null && row.OutputId == outputId && row.MessageId == context.MessageId)
                {
                    File.Delete(path);
                    AgentDiagnostics.ForwardResolved.Add(1, new KeyValuePair<string, object?>("outputId", outputId));
                    return;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ForwardEntry>> FetchDueAsync(int max, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var due = new List<ForwardEntry>();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var path in RowFiles())
            {
                if (due.Count >= max)
                    break;

                var row = await ReadRowAsync(path, cancellationToken).ConfigureAwait(false);
                if (row is null || row.Status != ForwardStatus.Pending || row.NextAttemptAt > now || row.LeasedUntil > now)
                    continue;

                var leased = row with { LeasedUntil = now.Add(_leaseDuration) };
                await WriteRowAsync(leased, cancellationToken).ConfigureAwait(false);
                due.Add(ToEntry(leased));
            }
        }
        finally
        {
            _gate.Release();
        }

        return due;
    }

    public Task RescheduleAsync(long id, int attempts, DateTimeOffset nextAttemptAt, string? lastError, CancellationToken cancellationToken)
        => UpdateAsync(id, row => row with
        {
            Attempts = attempts,
            NextAttemptAt = nextAttemptAt,
            LeasedUntil = null,
            LastError = lastError,
        }, cancellationToken);

    public async Task ParkAsync(long id, string reason, CancellationToken cancellationToken)
    {
        var outputId = 0;
        await UpdateAsync(id, row =>
        {
            outputId = row.OutputId;
            return row with { Status = ForwardStatus.Parked, LeasedUntil = null, LastError = reason };
        }, cancellationToken).ConfigureAwait(false);
        if (outputId != 0)
            AgentDiagnostics.ForwardParked.Add(1, new KeyValuePair<string, object?>("outputId", outputId));
    }

    public async Task<IReadOnlyList<ForwardEntry>> ListAsync(ForwardStatus? status, int max, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = new List<ForwardEntry>();
            foreach (var path in RowFiles())
            {
                if (entries.Count >= max)
                    break;
                var row = await ReadRowAsync(path, cancellationToken).ConfigureAwait(false);
                if (row is not null && (status is null || row.Status == status))
                    entries.Add(ToEntry(row));
            }
            return entries;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<bool> RequeueAsync(long id, CancellationToken cancellationToken)
        => UpdateExistingAsync(id, row => row with
        {
            Status = ForwardStatus.Pending,
            NextAttemptAt = DateTimeOffset.UtcNow,
            LeasedUntil = null,
            LastError = null,
        }, cancellationToken);

    public async Task<bool> DiscardAsync(long id, string? reason, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = PathFor(id);
            if (!File.Exists(path))
                return false;
            File.Delete(path);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task UpdateAsync(long id, Func<Row, Row> update, CancellationToken cancellationToken)
    {
        _ = await UpdateExistingAsync(id, update, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> UpdateExistingAsync(long id, Func<Row, Row> update, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = PathFor(id);
            if (!File.Exists(path))
                return false;
            var row = await ReadRowAsync(path, cancellationToken).ConfigureAwait(false);
            if (row is null)
                return false;
            await WriteRowAsync(update(row), cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task WriteRowAsync(Row row, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var path = PathFor(row.Id);
        var temp = path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, row, JsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temp, path, overwrite: true);
    }

    private static async Task<Row?> ReadRowAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, useAsync: true);
            return await JsonSerializer.DeserializeAsync<Row>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private IEnumerable<string> RowFiles()
        => Directory.EnumerateFiles(_directory, "*.json").OrderBy(static p => p, StringComparer.Ordinal);

    private long NextIdUnsafe(DateTimeOffset now)
    {
        var id = now.UtcTicks;
        while (File.Exists(PathFor(id)))
            id++;
        return id;
    }

    private string PathFor(long id) => Path.Combine(_directory, FormattableString.Invariant($"{id:D20}.json"));

    private ForwardEntry ToEntry(Row row)
        => new(
            row.Id,
            _protector.Unprotect(row.EncryptedMessage),
            row.OutputId,
            row.Status,
            row.Attempts,
            row.NextAttemptAt,
            row.LastError,
            row.CreatedAt);
}
