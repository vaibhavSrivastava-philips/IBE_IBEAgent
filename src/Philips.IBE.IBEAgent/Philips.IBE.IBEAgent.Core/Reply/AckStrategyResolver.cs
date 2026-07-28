using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;

namespace Philips.IBE.IBEAgent.Core;

// §6/§8 — resolves the one reply mode a contract declares (Ack XOR Response, validated by
// ContractOptionsValidator) into the (IAckStrategy, timeout) pair the ReplyContextFactory needs.
// Kept out of ContractCompiler because reply policy is orthogonal to topology/legs assembly.
public static class AckStrategyResolver
{
    public static (IAckStrategy Strategy, TimeSpan Timeout) Resolve(ContractOptions contract, ComponentRegistry registry)
    {
        if (contract.Response.IsEnabled)
            return (new ResponseReplyStrategy(), TimeSpan.FromMilliseconds(contract.Response.TimeoutMs));

        if (!contract.Acknowledgement.IsEnabled)
            return (new NoAckStrategy(), Timeout.InfiniteTimeSpan);   // fire-and-forget: no reply bytes written

        if (contract.Acknowledgement.IsEnhanced)
            return (new EnhancedAckStrategy(registry, contract.Acknowledgement.Shape), Timeout.InfiniteTimeSpan);

        return (new NormalAckStrategy(), Timeout.InfiniteTimeSpan);   // default: Normal ack, fires on receipt
    }
}
