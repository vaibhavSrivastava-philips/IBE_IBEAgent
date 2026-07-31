using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// §6.1 — Request-reply: writes the responder leg's CAPTURED payload instead of a generated ack.
// A contract using Response mode has exactly one required leg (the responder, ResponseOptions.
// FromOutputId or the sole required output); ReplyContext.ReportLeg fires this strategy once that
// leg's terminal DeliveryResult arrives, so ResponsePayload already carries the peer's bytes
// (e.g. TcpOutboundEndpoint's MLLP ack-as-response when ExpectReply=true). On failure/timeout a
// protocol error reply is written instead (no response payload was captured).
public sealed class ResponseReplyStrategy : IAckStrategy
{
    public bool RepliesOnReceipt => false;   // must wait for the responder leg's captured payload

    public Task WriteReplyAsync(MessageContext context, DeliveryResult result)
    {
        if (result.Outcome is DeliveryOutcome.Delivered or DeliveryOutcome.Accepted
            && !result.ResponsePayload.IsEmpty)
        {
            return context.Ack.WriteAsync(result.ResponsePayload, CancellationToken.None);
        }

        var error = result.Error ?? "no response received";
        var protocolError = System.Text.Encoding.UTF8.GetBytes($"ERR|{error}");
        return context.Ack.WriteAsync(protocolError, CancellationToken.None);
    }
}
