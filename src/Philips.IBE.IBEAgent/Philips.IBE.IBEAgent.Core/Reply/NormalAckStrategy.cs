using System.Text;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// Normal ack: reply "received" as soon as the message is accepted (on fan-out), independent of delivery.
// STUB: writes a fixed AA. The real per-format ACK (Hl7SingleAckFormatter, keyed by Format x Shape) is Phase 5.
public sealed class NormalAckStrategy : IAckStrategy
{
    private static readonly byte[] StubAck = Encoding.UTF8.GetBytes("MSA|AA|received");

    public bool RepliesOnReceipt => true;   // fire on receipt, not after delivery

    public Task WriteReplyAsync(MessageContext context, DeliveryResult result)
        => context.Ack.WriteAsync(StubAck, CancellationToken.None);   // Phase 5: IAckFormatter renders real bytes
}