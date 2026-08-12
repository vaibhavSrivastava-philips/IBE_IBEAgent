using System.Text;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Formats.Hl7;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Formats.Hl7.UnitTests;

public sealed class Hl7BatchAckFormatterTests
{
    [Fact]
    public void Render_wraps_generated_single_ack_in_batch_envelope()
    {
        var formatter = new Hl7BatchAckFormatter();
        var context = MessageContextBuilder.Create(payload: "MSH|^~\\&|SND|SF|RCV|RF|20240101000000||ADT^A01|CTRL1|P|2.5\rPID|1");

        var bytes = formatter.Render(context, new DeliveryResult(DeliveryOutcome.Delivered));
        var text = Encoding.UTF8.GetString(bytes.Span);

        Assert.StartsWith("BHS|", text);
        Assert.Contains("\rMSH|", text);
        Assert.Contains("\rMSA|AA|CTRL1", text);
        Assert.EndsWith("BTS|1", text);
    }

    [Fact]
    public void Render_multiple_results_wraps_each_result_in_one_batch()
    {
        var formatter = new Hl7BatchAckFormatter();
        var context = MessageContextBuilder.Create(payload: "MSH|^~\\&|SND|SF|RCV|RF|20240101000000||ADT^A01|CTRL2|P|2.5\rPID|1");
        var downstreamAck = Encoding.UTF8.GetBytes("MSH|^~\\&|D|D|S|S|20240101000000||ACK|ACK1|P|2.5\rMSA|AA|DOWNSTREAM");

        var bytes = formatter.Render(context,
        [
            new DeliveryResult(DeliveryOutcome.Delivered, ResponsePayload: downstreamAck),
            new DeliveryResult(DeliveryOutcome.Failed, "failed leg"),
        ]);
        var text = Encoding.UTF8.GetString(bytes.Span);

        Assert.StartsWith("BHS|", text);
        Assert.Contains("\rMSA|AA|DOWNSTREAM", text);
        Assert.Contains("\rMSA|AE|CTRL2", text);
        Assert.EndsWith("BTS|2", text);
    }
}
