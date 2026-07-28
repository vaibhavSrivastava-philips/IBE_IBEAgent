using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Formats.Hl7;

// hl7v2 IMessageCodec: the canonical payload for an hl7v2-sourced message IS already MLLP-framable
// HL7 wire bytes (INV-1/INV-2 single-format contracts), so this leg encoding is pass-through.
// A future non-hl7v2 destination format would live behind its own registered codec (OCP).
public sealed class Hl7v2Codec : IMessageCodec
{
    public ReadOnlyMemory<byte> Encode(MessageContext context) => context.Payload;
}
