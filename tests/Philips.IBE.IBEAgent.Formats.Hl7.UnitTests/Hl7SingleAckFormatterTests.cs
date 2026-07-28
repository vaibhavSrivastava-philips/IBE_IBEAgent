using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Formats.Hl7.UnitTests;

public sealed class Hl7SingleAckFormatterTests
{
    [Fact]
    public void Format_and_shape_match_hl7v2_single()
    {
        var formatter = new Hl7SingleAckFormatter();

        Assert.Equal(MessageFormats.Hl7v2, formatter.Format);
        Assert.Equal(AckShape.Single, formatter.Shape);
    }

    [Fact]
    public void Delivered_renders_AA()
    {
        var formatter = new Hl7SingleAckFormatter();
        var ctx = MessageContextBuilder.Create();

        var bytes = formatter.Render(ctx, new DeliveryResult(DeliveryOutcome.Delivered));

        Assert.Contains("AA", System.Text.Encoding.UTF8.GetString(bytes.Span));
    }

    [Fact]
    public void Failed_renders_AE()
    {
        var formatter = new Hl7SingleAckFormatter();
        var ctx = MessageContextBuilder.Create();

        var bytes = formatter.Render(ctx, new DeliveryResult(DeliveryOutcome.Failed, "boom"));

        Assert.Contains("AE", System.Text.Encoding.UTF8.GetString(bytes.Span));
    }
}
