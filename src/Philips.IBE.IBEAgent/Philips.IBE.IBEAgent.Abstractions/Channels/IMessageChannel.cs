namespace Philips.IBE.IBEAgent.Abstractions;

// the single queue+durability seam, used by every per-input ingress queue and every per-leg queue.
public interface IMessageChannel
{
    ValueTask EnqueueAsync(MessageContext context, CancellationToken cancellationToken);
    IAsyncEnumerable<MessageContext> ReadAllAsync(CancellationToken cancellationToken);
    void Complete();
}