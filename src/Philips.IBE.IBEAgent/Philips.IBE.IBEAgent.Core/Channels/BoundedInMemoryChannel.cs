using System.Threading.Channels;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// AtMostOnce in-memory queue. Bounded; overflow is Wait (async backpressure) or Reject (fast-fail).
// SpillToDisk is a DURABLE concern handled by DurableChannel (Phase 6), not here.
public sealed class BoundedInMemoryChannel : IMessageChannel
{
    private readonly Channel<MessageContext> _channel;
    private readonly OverflowPolicy _overflow;

    public BoundedInMemoryChannel(int capacity, OverflowPolicy overflow = OverflowPolicy.Wait)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be > 0 (queues are always bounded, P4).");
        if (overflow == OverflowPolicy.SpillToDisk)
            throw new NotSupportedException("SpillToDisk requires a durable channel (DurableChannel, Phase 6).");

        _overflow = overflow;
        _channel = Channel.CreateBounded<MessageContext>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,  // WriteAsync => Wait; TryWrite => Reject at the call site
            SingleReader = false,                    // lets DegreeOfParallelism > 1 add readers later
            SingleWriter = false,                    // fan-out + dispatcher enqueue concurrently
        });
    }

    public ValueTask EnqueueAsync(MessageContext context, CancellationToken cancellationToken)
    {
        if (_overflow == OverflowPolicy.Reject)
        {
            return _channel.Writer.TryWrite(context)
                ? ValueTask.CompletedTask
                : ValueTask.FromException(new QueueFullException("Queue is full and OverflowPolicy is Reject."));
        }

        return _channel.Writer.WriteAsync(context, cancellationToken); // Wait = async backpressure
    }

    public IAsyncEnumerable<MessageContext> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    public void Complete() => _channel.Writer.Complete();
}