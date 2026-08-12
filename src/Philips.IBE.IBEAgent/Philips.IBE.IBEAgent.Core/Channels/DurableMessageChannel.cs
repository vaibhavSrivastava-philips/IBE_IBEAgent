using System.Text.Json;
using System.Threading.Channels;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

public sealed class DurableMessageChannel : IMessageChannel
{
    private readonly Channel<MessageContext> _channel;
    private readonly string _directory;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public DurableMessageChannel(int capacity, string directory)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be > 0 (queues are always bounded, P4).");
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        _directory = directory;
        Directory.CreateDirectory(_directory);
        _channel = Channel.CreateBounded<MessageContext>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    public async ValueTask EnqueueAsync(MessageContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var record = DurableMessageRecord.From(context);
        var path = GetPath(context.MessageId);
        var tempPath = path + ".tmp";
        await using (var stream = System.IO.File.Create(tempPath))
            await JsonSerializer.SerializeAsync(stream, record, _jsonOptions, cancellationToken);
        System.IO.File.Move(tempPath, path, overwrite: true);

        try
        {
            await _channel.Writer.WriteAsync(context, cancellationToken);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    public async IAsyncEnumerable<MessageContext> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var context in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            TryDelete(GetPath(context.MessageId));
            yield return context;
        }
    }

    public void Complete() => _channel.Writer.Complete();

    public int PersistedCount => Directory.EnumerateFiles(_directory, "*.json").Count();

    private string GetPath(Guid messageId) => Path.Combine(_directory, messageId.ToString("N") + ".json");

    private static void TryDelete(string path)
    {
        try { System.IO.File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record DurableMessageRecord(
        Guid MessageId,
        string CorrelationId,
        int SourceEndpointId,
        string Format,
        int LegOutputId,
        bool IsReplay,
        Dictionary<string, string> Headers,
        byte[] Payload)
    {
        public static DurableMessageRecord From(MessageContext context) => new(
            context.MessageId,
            context.CorrelationId,
            context.SourceEndpointId,
            context.Format,
            context.LegOutputId,
            context.IsReplay,
            new Dictionary<string, string>(context.Headers, StringComparer.Ordinal),
            context.Payload.ToArray());
    }
}
