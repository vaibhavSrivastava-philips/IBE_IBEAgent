namespace Philips.IBE.IBEAgent.Abstractions;

// §3.8/§6 — FORMAT+SHAPE: keyed by (Format x Shape); renders a GENERATED ack in the source's own type (INV-4).
public interface IAckFormatter
{
    string Format { get; }                 // e.g. "hl7v2"
    AckShape Shape { get; }                // Single | Batch
    ReadOnlyMemory<byte> Render(MessageContext context, in DeliveryResult result);
}