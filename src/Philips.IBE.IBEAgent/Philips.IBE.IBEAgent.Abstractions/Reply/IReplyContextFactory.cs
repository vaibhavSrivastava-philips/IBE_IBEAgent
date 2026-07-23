namespace Philips.IBE.IBEAgent.Abstractions;

// Mints the per-message reply authority AT RECEPTION without the inbound endpoint
// knowing topology/policy. Implemented in Core: resolves the source's contract
// reply policy (strategy + formatter + timeout) and returns a ready IReplyContext bound to the token.
public interface IReplyContextFactory
{
    IReplyContext Create(int sourceEndpointId, IAckToken ackToken);
}