using System.Text;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// §3.8/§6 — Enhanced ack: forwards the downstream system's OWN acknowledgement back to the source
// end to end (fires only after ReportLeg settles the reply, i.e. RepliesOnReceipt = false). For a
// single required output the destination already returned its ack (captured in
// DeliveryResult.ResponsePayload — e.g. the MLLP ack of a TCP leg with ExpectReply, or an HTTP
// leg's response body), so that ack is relayed verbatim: the source sees exactly what the receiver
// sent. Only when nothing was captured (the leg failed, or the destination returns no reply bytes)
// does the strategy synthesize an ack that still reflects the delivery outcome, via the source's
// own IAckFormatter (Format x AckShape, INV-4) or a fixed AA/AE stub if no formatter is registered.
// NOTE: aggregating multiple downstream acks for a fan-out contract is a separate concern and is
// not handled here yet; for multi-output enhanced contracts the settling leg's ack is relayed.
public sealed class EnhancedAckStrategy : IAckStrategy
{
    private static readonly byte[] StubNack = Encoding.UTF8.GetBytes("MSA|AE|delivery failed");

    private readonly ComponentRegistry _registry;
    private readonly AckShape _shape;

    public EnhancedAckStrategy(ComponentRegistry registry, AckShape shape)
    {
        _registry = registry;
        _shape = shape;
    }

    public bool RepliesOnReceipt => false;   // wait for delivery outcome, not receipt

    public Task WriteReplyAsync(MessageContext context, DeliveryResult result)
    {
        // Relay the destination's own ack end to end when it delivered and returned reply bytes.
        if (result.Outcome is DeliveryOutcome.Delivered or DeliveryOutcome.Accepted
            && !result.ResponsePayload.IsEmpty)
        {
            return context.Ack.WriteAsync(result.ResponsePayload, CancellationToken.None);
        }

        // No downstream ack captured (failure, or the destination sends none): synthesize one.
        var bytes = _registry.TryGetAckFormatter(context.Format, _shape, out var formatter) && formatter is not null
            ? formatter.Render(context, result)
            : FallbackBytes(result);
        return context.Ack.WriteAsync(bytes, CancellationToken.None);
    }

    private static ReadOnlyMemory<byte> FallbackBytes(DeliveryResult result)
        => result.Outcome is DeliveryOutcome.Delivered or DeliveryOutcome.Accepted
            ? Encoding.UTF8.GetBytes("MSA|AA|delivered")
            : StubNack;
}
