using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// §6/§8 — per-contract reply policy dispatch: sourceEndpointId -> (strategy, timeout), because
// each contract declares exactly one reply mode (Ack XOR Response) but a host runs many contracts
// side by side over the shared IReplyContextFactory seam used by every inbound endpoint.
public sealed class PerSourceReplyContextFactory : IReplyContextFactory
{
    private readonly IReadOnlyDictionary<int, ReplyPolicy> _bySource;

    public PerSourceReplyContextFactory(IReadOnlyDictionary<int, ReplyPolicy> bySource)
        => _bySource = bySource;

    public IReplyContext Create(int sourceEndpointId, IAckToken ackToken)
    {
        if (!_bySource.TryGetValue(sourceEndpointId, out var policy))
            throw new KeyNotFoundException($"No reply policy registered for source endpoint {sourceEndpointId}.");
        return new ReplyContext(policy.Strategy, policy.Timeout, policy.ReplyOnFilter);
    }
}
