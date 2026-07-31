using System.Text;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// Normal ack: reply "received" as soon as the message is accepted (on fan-out), independent of
// delivery. Renders the real ack via the source's own IAckFormatter (Format x AckShape, INV-4) so
// Normal-ack contracts emit proper HL7 ACKs; falls back to a fixed positive MSA only when no
// formatter is registered for the source's (Format, Shape).
public sealed class NormalAckStrategy : IAckStrategy
{
    private static readonly byte[] FallbackAck = Encoding.UTF8.GetBytes("MSA|AA|received");

    private readonly ComponentRegistry _registry;
    private readonly AckShape _shape;

    public NormalAckStrategy(ComponentRegistry registry, AckShape shape)
    {
        _registry = registry;
        _shape = shape;
    }

    public bool RepliesOnReceipt => true;   // fire on receipt, not after delivery

    public Task WriteReplyAsync(MessageContext context, DeliveryResult result)
    {
        var bytes = _registry.TryGetAckFormatter(context.Format, _shape, out var formatter) && formatter is not null
            ? formatter.Render(context, result)
            : FallbackAck;
        return context.Ack.WriteAsync(bytes, CancellationToken.None);
    }
}