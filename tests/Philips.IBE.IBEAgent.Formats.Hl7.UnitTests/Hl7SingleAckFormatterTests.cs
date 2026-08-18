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
    public void Unparseable_source_renders_AE_nack_with_msh_and_hint()
    {
        var formatter = new Hl7SingleAckFormatter();
        var ctx = MessageContextBuilder.Create(payload: "not-an-hl7-message");   // unparseable -> fallback

        var ack = System.Text.Encoding.UTF8.GetString(
            formatter.Render(ctx, new DeliveryResult(DeliveryOutcome.Delivered)).Span);

        Assert.Contains("MSH", ack);
        Assert.Contains("MSA|AE", ack);                 // a parse failure is always a negative ack
        Assert.Contains("Unable to parse", ack);        // MSA-3 hint
        Assert.Contains("ERR", ack);                    // coded error segment
        Assert.Contains(ctx.CorrelationId, ack);        // correlation id stands in for MSA-2
    }

    // A real HL7 v2 message so the ACK is built via the ported HL7AckGenerator (MSH echoed, the
    // original MSH-10 control id carried into MSA-2) rather than the unparseable-input fallback.
    private const string Hl7Message =
        "MSH|^~\\&|SEND_APP|SEND_FAC|RECV_APP|RECV_FAC|20260101120000||ADT^A01|CTRL12345|P|2.3\r" +
        "EVN|A01|20260101120000\r" +
        "PID|1||PATID1234^5^M11||DOE^JOHN";

    [Fact]
    public void Real_message_delivered_renders_MSH_and_MSA_AA_with_control_id()
    {
        var formatter = new Hl7SingleAckFormatter();
        var ctx = MessageContextBuilder.Create(payload: Hl7Message);

        var ack = System.Text.Encoding.UTF8.GetString(
            formatter.Render(ctx, new DeliveryResult(DeliveryOutcome.Delivered)).Span);

        Assert.Contains("MSH", ack);
        Assert.Contains("MSA|AA", ack);
        Assert.Contains("CTRL12345", ack);   // original control id echoed into MSA-2
    }

    [Fact]
    public void Real_message_failed_renders_MSA_AE()
    {
        var formatter = new Hl7SingleAckFormatter();
        var ctx = MessageContextBuilder.Create(payload: Hl7Message);

        var ack = System.Text.Encoding.UTF8.GetString(
            formatter.Render(ctx, new DeliveryResult(DeliveryOutcome.Failed, "boom")).Span);

        Assert.Contains("MSA|AE", ack);
        Assert.Contains("boom", ack);
    }

    [Fact]
    public void Real_message_filtered_renders_MSA_AR_reject_with_reason()
    {
        var formatter = new Hl7SingleAckFormatter();
        var ctx = MessageContextBuilder.Create(payload: Hl7Message);

        var ack = System.Text.Encoding.UTF8.GetString(
            formatter.Render(ctx, new DeliveryResult(DeliveryOutcome.Filtered, "care-unit-not-permitted")).Span);

        Assert.Contains("MSA|AR", ack);                     // intentional reject, not a delivery error (AE)
        Assert.Contains("care-unit-not-permitted", ack);    // reason carried back to the sender
    }
}
