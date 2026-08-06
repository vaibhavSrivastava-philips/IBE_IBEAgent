using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Formats.Hl7;

// STUB — NOT IMPLEMENTED. Placeholder for the HL7 batch ack (AckShape.Batch), captured now so the
// intended behavior is documented; contracts with AckShape.Batch are rejected at config validation
// (ContractOptionsValidator) until this is built.
//
// WHAT IT WILL DO (§6; HL7 v2 batch protocol BHS..BTS):
//   Render ONE batch acknowledgement that wraps N units — one MSH/MSA[/ERR] per unit — inside a
//   BHS (batch header) ... BTS (batch trailer, count = N) envelope. A "unit" is one settled item:
//     - multi-output enhanced ack -> one unit per REQUIRED OUTPUT LEG (this engine's fan-out case)
//     - inbound BHS..BTS batch     -> one unit per INBOUND MESSAGE (de-batched at the input)
//   Both feed the SAME renderer; only the source of the N results differs.
//
// PER-UNIT RULE:
//   - delivered AND returned ack bytes  -> embed that downstream ack verbatim (relay)
//   - delivered but returned no bytes    -> generate a positive MSA (AA) via HL7AckGenerator
//   - FAILED, or UNARRIVED at timeout    -> generate a negative MSA (AE) NACK
//     (HL7AckGenerator.GenerateHL7Reject / BuildFallbackNack), so the batch is always complete.
//   Units are ordered deterministically (by OutputId).
//
// EDGE CASES:
//   - a single unit -> a degenerate batch of one (still BHS..BTS with BTS|1)
//   - all failed     -> a batch of NACKs
//   - unparseable downstream ack -> fall back to a generated AE unit (the reply path never throws)
//   - MSH echo / control-id per unit follows the same rules as Hl7SingleAckFormatter
//
// WIRING NOTES (when built):
//   - IAckFormatter.Render is single-result; a batch needs the whole set, so add a batch-capable seam
//     (e.g. IBatchAckFormatter.Render(MessageContext, IReadOnlyList<DeliveryResult>)) that this class
//     implements. Register under (MessageFormats.Hl7v2, AckShape.Batch) in ComponentRegistryBuilder.
//   - EnhancedAckStrategy's Delivered branch dispatches to the batch renderer when Shape == Batch.
//   - ReplyContext must WAIT FOR ALL required legs for Batch (no short-circuit on the first failure);
//     add a strategy flag (e.g. IAckStrategy.WaitsForAllLegs => true for Batch) that ReplyContext honors.
//   - Remove the AckShape.Batch fail-fast guard in ContractOptionsValidator.
public sealed class Hl7BatchAckFormatter
{
    public string Format => MessageFormats.Hl7v2;
    public AckShape Shape => AckShape.Batch;

    // Intended: wrap each unit's ack (relayed or generated) in a BHS..BTS envelope. See class remarks.
    public ReadOnlyMemory<byte> Render(MessageContext context, IReadOnlyList<DeliveryResult> results)
        => throw new NotImplementedException("HL7 batch ack (BHS..BTS) is not implemented yet.");
}
