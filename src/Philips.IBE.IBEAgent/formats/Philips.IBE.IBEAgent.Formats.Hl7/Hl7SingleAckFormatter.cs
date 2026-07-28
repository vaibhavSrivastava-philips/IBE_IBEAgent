using System.Text;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Formats.Hl7;

// §3.8/§6 — keyed by (Format x Shape); renders a GENERATED ack in HL7's own MSA segment shape (INV-4).
// STUB body: real MSH/MSA field mirroring (control id, version) is a later refinement of this class,
// not a new seam — Component Registry name resolution and the IAckFormatter contract are already final.
public sealed class Hl7SingleAckFormatter : IAckFormatter
{
    public string Format => MessageFormats.Hl7v2;
    public AckShape Shape => AckShape.Single;

    public ReadOnlyMemory<byte> Render(MessageContext context, in DeliveryResult result)
    {
        var code = result.Outcome == DeliveryOutcome.Delivered || result.Outcome == DeliveryOutcome.Accepted
            ? "AA"
            : "AE";
        return Encoding.UTF8.GetBytes($"MSA|{code}|{context.CorrelationId}");
    }
}
