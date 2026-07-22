using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.TestKit;

public sealed class FakeMessageCodec : IMessageCodec
{
    // Pass-through: canonical bytes are already the wire form (raw/same-format leg).
    public ReadOnlyMemory<byte> Encode(MessageContext context) => context.Payload;
}