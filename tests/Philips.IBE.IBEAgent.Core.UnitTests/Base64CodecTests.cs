using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class Base64CodecTests
{
    [Fact]
    public void Encode_decodes_base64_payload_to_raw_bytes()
    {
        var raw = new byte[] { 1, 2, 3, 250, 99 };
        var ctx = MessageContextBuilder.Create(payload: Convert.ToBase64String(raw));

        Assert.Equal(raw, new Base64Codec().Encode(ctx).ToArray());
    }

    [Fact]
    public void Encode_throws_on_non_base64_payload()
    {
        var ctx = MessageContextBuilder.Create(payload: "not base64 @@@");
        Assert.Throws<FormatException>(() => new Base64Codec().Encode(ctx));
    }
}
