using System.Text;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// §3.8/§6 — Enhanced ack: reflects the REAL delivery outcome (fires only after ReportLeg settles
// the reply, i.e. RepliesOnReceipt = false). A positive ack is rendered by the source's own
// IAckFormatter (Format x AckShape, INV-4); a failure renders a negative ack via the same
// formatter (formatters key off DeliveryResult.Outcome to choose AA/AE, so no separate NACK path
// is needed here). The formatter is resolved per-message from the source's own Format tag (INV-1),
// since one contract can have multiple input sources/formats sharing the same Ack policy. Falls
// back to a fixed AE/AA stub if no formatter is registered for that Format/Shape (keeps the
// contract usable before Formats.* plug-ins are registered).
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
