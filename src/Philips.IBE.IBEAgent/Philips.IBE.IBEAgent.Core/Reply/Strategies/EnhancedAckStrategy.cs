using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// §3.8/§6 — Enhanced ack: reflects the real delivery outcome, firing only after ReplyContext settles
// the required legs (RepliesOnReceipt = false). SINGLE shape (the only shape today): on success the
// SOURCE sees a downstream ack verbatim — the FIRST required leg (by OutputId order) that returned ack
// bytes is relayed; only if none captured any does it GENERATE one via the source's own IAckFormatter
// (Format x Shape). On any required failure (or timeout) ONE negative ACK is generated. The neutral IBE
// marker is a LAST RESORT, reached only when no formatter is registered for the source's (Format,
// Shape) — Core can't render a protocol ack itself (that lives in the plug-in) — and it logs a warning
// (once per contract) so the missing formatter is visible in ops; the reply path never throws.
public sealed class EnhancedAckStrategy : IAckStrategy
{
    private static readonly byte[] NoFormatterAck = Encoding.UTF8.GetBytes("IBE:ACK (no ack formatter)");
    private static readonly byte[] NoFormatterNack = Encoding.UTF8.GetBytes("IBE:NACK (no ack formatter)");

    private readonly ComponentRegistry _registry;
    private readonly AckShape _shape;
    private readonly ILogger<EnhancedAckStrategy> _logger;
    private int _warnedNoFormatter;

    public EnhancedAckStrategy(ComponentRegistry registry, AckShape shape, ILogger<EnhancedAckStrategy>? logger = null)
    {
        _registry = registry;
        _shape = shape;
        _logger = logger ?? NullLogger<EnhancedAckStrategy>.Instance;
    }

    public bool RepliesOnReceipt => false;   // wait for delivery outcome, not receipt

    public Task WriteReplyAsync(MessageContext context, ReplyOutcome outcome)
    {
        if (outcome.Outcome is DeliveryOutcome.Delivered)
        {
            // Single: relay the first required leg's captured ack (results are ordered by OutputId).
            foreach (var leg in outcome.LegResults)
            {
                if (!leg.ResponsePayload.IsEmpty)
                    return context.Ack.WriteAsync(leg.ResponsePayload, CancellationToken.None);
            }

            // Delivered but no downstream ack bytes captured -> generate a positive ack.
            return WriteGenerated(context, new DeliveryResult(DeliveryOutcome.Delivered), NoFormatterAck);
        }

        // Failure/timeout/filtered -> one generated negative ACK reflecting the outcome.
        return WriteGenerated(context, new DeliveryResult(outcome.Outcome, outcome.Reason), NoFormatterNack);
    }

    private Task WriteGenerated(MessageContext context, in DeliveryResult result, byte[] noFormatterFallback)
    {
        if (_registry.TryGetAckFormatter(context.Format, _shape, out var formatter) && formatter is not null)
            return context.Ack.WriteAsync(formatter.Render(context, result), CancellationToken.None);

        WarnMissingFormatterOnce(context);
        return context.Ack.WriteAsync(noFormatterFallback, CancellationToken.None);
    }

    // Warn once per contract instance so a missing formatter is visible without per-message log spam.
    private void WarnMissingFormatterOnce(MessageContext context)
    {
        if (Interlocked.Exchange(ref _warnedNoFormatter, 1) != 0) return;
        _logger.LogWarning(
            "No ack formatter registered for (Format {Format}, Shape {Shape}); source {SourceEndpointId} receives a neutral IBE placeholder reply instead of a generated ack.",
            context.Format, _shape, context.SourceEndpointId);
    }
}
