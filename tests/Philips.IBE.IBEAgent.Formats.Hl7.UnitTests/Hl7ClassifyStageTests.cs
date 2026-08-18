using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Formats.Hl7.UnitTests;

public sealed class Hl7ClassifyStageTests
{
    [Fact]
    public async Task Classifies_type_and_control_id_into_headers()
    {
        var stage = new Hl7ClassifyStage();
        var ctx = MessageContextBuilder.Create(payload: "MSH|^~\\&|APP|FAC|DEST|DFAC|20260806120000||ADT^A01|CTRL123|P|2.5");

        var result = await stage.ProcessAsync(ctx);

        Assert.False(result.Filtered);
        Assert.Equal("ADT^A01", ctx.Headers[Hl7ClassifyStage.MessageTypeHeader]);
        Assert.Equal("CTRL123", ctx.Headers[Hl7ClassifyStage.MessageIdHeader]);
    }

    [Fact]
    public async Task Non_hl7_message_passes_through_without_headers()
    {
        var stage = new Hl7ClassifyStage();
        var ctx = MessageContextBuilder.Create(payload: "not an hl7 message");

        var result = await stage.ProcessAsync(ctx);

        Assert.False(result.Filtered);
        Assert.False(ctx.Headers.ContainsKey(Hl7ClassifyStage.MessageTypeHeader));
        Assert.False(ctx.Headers.ContainsKey(Hl7ClassifyStage.MessageIdHeader));
    }
}
