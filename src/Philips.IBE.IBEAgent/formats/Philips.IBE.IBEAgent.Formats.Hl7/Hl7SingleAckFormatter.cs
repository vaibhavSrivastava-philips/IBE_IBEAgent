using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Formats.Hl7;

// §3.8/§6 — keyed by (Format x Shape); renders a GENERATED ack in HL7's own type (INV-4). Builds the
// ACK from the source message via HL7AckGenerator so MSH is echoed/swapped and the original MSH-10
// control id is carried into MSA-2 (the real legacy behavior). Falls back to a minimal MSA only when
// the source can't be parsed as HL7 (e.g. a non-HL7 probe or a corrupt frame), logging the failure so
// the degraded ack is visible in ops; the reply path itself never throws.
public sealed class Hl7SingleAckFormatter : IAckFormatter
{
    private readonly ILogger<Hl7SingleAckFormatter> _logger;

    public Hl7SingleAckFormatter(ILogger<Hl7SingleAckFormatter>? logger = null)
        => _logger = logger ?? NullLogger<Hl7SingleAckFormatter>.Instance;

    public string Format => MessageFormats.Hl7v2;
    public AckShape Shape => AckShape.Single;

    public ReadOnlyMemory<byte> Render(MessageContext context, in DeliveryResult result)
    {
        var source = Encoding.UTF8.GetString(context.Payload.Span);

        try
        {
            // Outcome -> HL7 ack code: delivered/accepted = AA; a FILTERED message is an intentional
            // reject = AR (carrying the filter reason); any other terminal outcome is an error = AE.
            var ack = result.Outcome switch
            {
                DeliveryOutcome.Delivered or DeliveryOutcome.Accepted => HL7AckGenerator.GenerateHL7Ack(source, true),
                DeliveryOutcome.Filtered => HL7AckGenerator.GenerateHL7Reject(source, "AR", result.Error),
                _ => HL7AckGenerator.GenerateHL7Ack(source, false),
            };
            return Encoding.UTF8.GetBytes(ack);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to parse the inbound HL7 message (source {SourceEndpointId}, correlation {CorrelationId}); returning an AE NACK.",
                context.SourceEndpointId,
                context.CorrelationId);

            // A message we can't parse is an error condition regardless of downstream delivery, so the
            // fallback is always a negative (AE) ack that hints the parse failure (MSA-3 + ERR).
            return Encoding.UTF8.GetBytes(
                HL7AckGenerator.BuildFallbackNack(context.CorrelationId, "Unable to parse inbound HL7 message"));
        }
    }
}
