using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// Slice-1 factory: one Normal-ack strategy for every source. Phase 3+ resolves the strategy/formatter
// per contract (Normal | Enhanced | Response) via the registries.
public sealed class ReplyContextFactory : IReplyContextFactory
{
    private readonly IAckStrategy _strategy;
    private readonly TimeSpan _timeout;

    public ReplyContextFactory(IAckStrategy strategy, TimeSpan? timeout = null)
    {
        _strategy = strategy;
        _timeout = timeout ?? Timeout.InfiniteTimeSpan;   // Normal ack fires on receipt, so timeout is inert here
    }

    // ackToken is passed for interface compliance / future strategies; this slice's ReplyContext obtains
    // the token via the attached message (message.Ack), so it isn't stored separately.
    public IReplyContext Create(int sourceEndpointId, IAckToken ackToken)
        => new ReplyContext(_strategy, _timeout);
}