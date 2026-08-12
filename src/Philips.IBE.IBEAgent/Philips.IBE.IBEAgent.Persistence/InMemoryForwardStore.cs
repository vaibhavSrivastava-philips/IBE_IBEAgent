using System.Collections.Concurrent;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Security;
using Philips.IBE.IBEAgent.Telemetry;

namespace Philips.IBE.IBEAgent.Persistence;

// §3.9 — one durable buffer, tagged by OutputId. This is the in-process/dev implementation:
// an in-memory table with the same row shape and lifecycle Postgres will eventually back
// (Id/Message/OutputId/Status/Attempts/NextAttemptAt/LastError/CreatedAt). Message is stored
// ENCRYPTED at rest (DPAPI via IDataProtector, machine-scoped per §3.9) and decrypted only when
// read back for replay. Swapping in a Postgres-backed IForwardStore later requires no change to
// DeliveryLeg, ForwardWorker, or the compiler — the seam is exactly this interface.
public sealed class InMemoryForwardStore : IForwardStore, IForwardStoreManagement
{
    private sealed class Row
    {
        public required long Id;
        public required byte[] EncryptedMessage;
        public required int OutputId;
        public ForwardStatus Status;
        public int Attempts;
        public DateTimeOffset NextAttemptAt;
        public DateTimeOffset? LeasedUntil;
        public string? LastError;
        public required DateTimeOffset CreatedAt;
    }

    private readonly ConcurrentDictionary<long, Row> _rows = new();
    private readonly IDataProtector _protector;
    private readonly TimeSpan _leaseDuration;
    private long _nextId;

    public InMemoryForwardStore(IDataProtector protector, TimeSpan? leaseDuration = null)
    {
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _leaseDuration = leaseDuration ?? TimeSpan.FromMinutes(5);
    }

    public Task StoreAsync(MessageContext context, int outputId, string? error, CancellationToken cancellationToken)
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

        var id = Interlocked.Increment(ref _nextId);
        var row = new Row
        {
            Id = id,
            EncryptedMessage = _protector.Protect(envelope.ToPlaintext()),
            OutputId = outputId,
            Status = ForwardStatus.Pending,
            Attempts = 0,
            NextAttemptAt = DateTimeOffset.UtcNow,
            LastError = error,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _rows[id] = row;
        AgentDiagnostics.ForwardStored.Add(1, new KeyValuePair<string, object?>("outputId", outputId));
        return Task.CompletedTask;
    }

    // Success -> delete (crash-safe outbox ordering: deliver -> confirm -> ResolveAsync, per §3.9).
    // Idempotent: resolving an already-resolved/unknown entry is a no-op.
    public Task ResolveAsync(MessageContext context, int outputId, CancellationToken cancellationToken)
    {
        foreach (var row in _rows.Values)
        {
            if (row.OutputId == outputId && MatchesMessage(row, context))
            {
                _rows.TryRemove(row.Id, out _);
                AgentDiagnostics.ForwardResolved.Add(1, new KeyValuePair<string, object?>("outputId", outputId));
                break;
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ForwardEntry>> FetchDueAsync(int max, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var due = _rows.Values
            .Where(r => r.Status == ForwardStatus.Pending && r.NextAttemptAt <= now && (r.LeasedUntil is null || r.LeasedUntil <= now))
            .OrderBy(r => r.NextAttemptAt)
            .Take(max)
            .Select(r =>
            {
                r.LeasedUntil = now.Add(_leaseDuration);
                return new ForwardEntry(
                    r.Id,
                    _protector.Unprotect(r.EncryptedMessage),
                    r.OutputId,
                    r.Status,
                    r.Attempts,
                    r.NextAttemptAt,
                    r.LastError,
                    r.CreatedAt);
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<ForwardEntry>>(due);
    }

    public Task RescheduleAsync(long id, int attempts, DateTimeOffset nextAttemptAt, string? lastError, CancellationToken cancellationToken)
    {
        if (_rows.TryGetValue(id, out var row))
        {
            row.Attempts = attempts;
            row.NextAttemptAt = nextAttemptAt;
            row.LeasedUntil = null;
            row.LastError = lastError;
        }
        return Task.CompletedTask;
    }

    // Terminal poison quarantine. Config drift (an OutputId that no longer resolves) also parks
    // here with a reason, never crashes the worker (§3.9 edge cases).
    public Task ParkAsync(long id, string reason, CancellationToken cancellationToken)
    {
        if (_rows.TryGetValue(id, out var row))
        {
            row.Status = ForwardStatus.Parked;
            row.LastError = reason;
            AgentDiagnostics.ForwardParked.Add(1, new KeyValuePair<string, object?>("outputId", row.OutputId));
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ForwardEntry>> ListAsync(ForwardStatus? status, int max, CancellationToken cancellationToken)
    {
        var rows = _rows.Values
            .Where(r => status is null || r.Status == status)
            .OrderBy(r => r.CreatedAt)
            .Take(max)
            .Select(ToEntry)
            .ToList();

        return Task.FromResult<IReadOnlyList<ForwardEntry>>(rows);
    }

    public Task<bool> RequeueAsync(long id, CancellationToken cancellationToken)
    {
        if (!_rows.TryGetValue(id, out var row))
            return Task.FromResult(false);

        row.Status = ForwardStatus.Pending;
        row.NextAttemptAt = DateTimeOffset.UtcNow;
        row.LeasedUntil = null;
        row.LastError = null;
        return Task.FromResult(true);
    }

    public Task<bool> DiscardAsync(long id, string? reason, CancellationToken cancellationToken)
    {
        var removed = _rows.TryRemove(id, out _);
        return Task.FromResult(removed);
    }

    private bool MatchesMessage(Row row, MessageContext context)
    {
        try
        {
            var envelope = ReplayEnvelope.FromPlaintext(_protector.Unprotect(row.EncryptedMessage));
            return envelope.MessageId == context.MessageId;
        }
        catch
        {
            return false;
        }
    }

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
