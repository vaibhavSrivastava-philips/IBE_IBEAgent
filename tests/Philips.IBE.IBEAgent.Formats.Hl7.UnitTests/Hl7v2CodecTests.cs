using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Formats.Hl7.UnitTests;

public sealed class Hl7v2CodecTests
{
    [Fact]
    public void Encode_is_pass_through_of_canonical_payload()
    {
        var codec = new Hl7v2Codec();
        var ctx = MessageContextBuilder.Create(payload: "MSH|^~\\&|...");

        var encoded = codec.Encode(ctx);

        Assert.True(encoded.Span.SequenceEqual(ctx.Payload.Span));
    }
}
