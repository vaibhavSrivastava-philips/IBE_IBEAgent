using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger<ResponseReplyStrategy> _logger;

    public ResponseReplyStrategy(ILogger<ResponseReplyStrategy>? logger = null)
        => _logger = logger ?? NullLogger<ResponseReplyStrategy>.Instance;

    public bool RepliesOnReceipt => false;   // must wait for the responder leg's captured payload

    public Task WriteReplyAsync(MessageContext context, ReplyOutcome outcome)
    {
        if (outcome.Outcome is DeliveryOutcome.Delivered)
        {
            foreach (var leg in outcome.LegResults)
            {
                if (!leg.ResponsePayload.IsEmpty)
                    return context.Ack.WriteAsync(leg.ResponsePayload, CancellationToken.None);
            }
        }

        // No usable response captured (delivery failed/timed out, or delivered with an empty body):
        // the request-reply peer gets a protocol error instead of the responder's bytes.
        var error = outcome.Reason ?? "no response received";
        _logger.LogWarning(
            "Request-reply for source {SourceEndpointId} (correlation {CorrelationId}) produced no response ({Reason}); writing a protocol error reply.",
            context.SourceEndpointId, context.CorrelationId, error);
        var protocolError = System.Text.Encoding.UTF8.GetBytes($"ERR|{error}");
        return context.Ack.WriteAsync(protocolError, CancellationToken.None);
    }
}
