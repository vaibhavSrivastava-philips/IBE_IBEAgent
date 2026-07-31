using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;

namespace Philips.IBE.IBEAgent.Core;

// §6/§8 — resolves the one reply mode a contract declares (Ack XOR Response, validated by
// ContractOptionsValidator) into the (IAckStrategy, timeout) pair the ReplyContextFactory needs.
// Kept out of ContractCompiler because reply policy is orthogonal to topology/legs assembly.
public static class AckStrategyResolver
{
    public static ReplyPolicy Resolve(ContractOptions contract, ComponentRegistry registry)
    {
        var replyOnFilter = contract.ReplyOnFilter ?? false;

        if (contract.Response.IsEnabled)
            return new ReplyPolicy(new ResponseReplyStrategy(), TimeSpan.FromMilliseconds(contract.Response.TimeoutMs), replyOnFilter);

        if (!contract.Acknowledgement.IsEnabled)
            return new ReplyPolicy(new NoAckStrategy(), Timeout.InfiniteTimeSpan, replyOnFilter);   // fire-and-forget: no reply bytes written

        if (contract.Acknowledgement.IsEnhanced)
            // §6 — Enhanced ack waits for delivery, so a hung required leg must eventually time out into a
            // NACK (the ReplyContext fires Failed on timeout). Normal/NoAck fire on receipt (or never), so
            // their wait stays infinite.
            return new ReplyPolicy(new EnhancedAckStrategy(registry, contract.Acknowledgement.Shape), ResolveAckTimeout(contract.Acknowledgement), replyOnFilter);

        return new ReplyPolicy(new NormalAckStrategy(registry, contract.Acknowledgement.Shape), Timeout.InfiniteTimeSpan, replyOnFilter);   // default: Normal ack, fires on receipt
    }

    private static TimeSpan ResolveAckTimeout(AckOptions ack)
        => ack.TimeoutMs > 0 ? TimeSpan.FromMilliseconds(ack.TimeoutMs) : Timeout.InfiniteTimeSpan;   // <=0 opts out
}
