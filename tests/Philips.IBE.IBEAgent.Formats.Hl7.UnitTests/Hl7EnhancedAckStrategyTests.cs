using System.Text;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Formats.Hl7.UnitTests;

public sealed class Hl7EnhancedAckStrategyTests
{
    private const string Hl7Message =
        "MSH|^~\\&|SEND_APP|SEND_FAC|RECV_APP|RECV_FAC|20260101120000||ADT^A01|CTRL12345|P|2.3\r" +
        "EVN|A01|20260101120000\r" +
        "PID|1||PATID1234^5^M11||DOE^JOHN";

    [Fact]
    public async Task Delivered_with_downstream_hl7_ack_relays_ack_verbatim()
    {
        var token = new FakeAckToken();
        var ctx = MessageContextBuilder.Create(payload: Hl7Message, ack: token);
        var downstreamAck = HL7AckGenerator.GenerateHL7Ack(Hl7Message, statusResponse: true);
        DeliveryResult[] legs = [new(DeliveryOutcome.Delivered, ResponsePayload: Encoding.UTF8.GetBytes(downstreamAck))];

        await CreateStrategy().WriteReplyAsync(ctx, ReplyOutcome.Delivered(legs));

        Assert.Equal(1, token.WriteCount);
        Assert.Equal(downstreamAck, Encoding.UTF8.GetString(token.Writes[0]));
        Assert.Contains("MSA|AA", Encoding.UTF8.GetString(token.Writes[0]));
    }

    [Fact]
    public async Task Delivered_with_downstream_hl7_nack_relays_nack_verbatim()
    {
        var token = new FakeAckToken();
        var ctx = MessageContextBuilder.Create(payload: Hl7Message, ack: token);
        var downstreamNack = HL7AckGenerator.GenerateHL7Reject(Hl7Message, "AE", "downstream rejected");
        DeliveryResult[] legs = [new(DeliveryOutcome.Delivered, ResponsePayload: Encoding.UTF8.GetBytes(downstreamNack))];

        await CreateStrategy().WriteReplyAsync(ctx, ReplyOutcome.Delivered(legs));

        Assert.Equal(1, token.WriteCount);
        Assert.Equal(downstreamNack, Encoding.UTF8.GetString(token.Writes[0]));
        Assert.Contains("MSA|AE", Encoding.UTF8.GetString(token.Writes[0]));
        Assert.Contains("downstream rejected", Encoding.UTF8.GetString(token.Writes[0]));
    }

    [Fact]
    public async Task Failed_delivery_generates_hl7_ae_with_failure_reason()
    {
        var token = new FakeAckToken();
        var ctx = MessageContextBuilder.Create(payload: Hl7Message, ack: token);

        await CreateStrategy().WriteReplyAsync(ctx, ReplyOutcome.Failed("downstream timeout", []));

        var ack = Encoding.UTF8.GetString(token.Writes[0]);
        Assert.Equal(1, token.WriteCount);
        Assert.Contains("MSA|AE", ack);
        Assert.Contains("CTRL12345", ack);
        Assert.Contains("downstream timeout", ack);
    }

    private static EnhancedAckStrategy CreateStrategy()
    {
        var registry = new ComponentRegistry()
            .RegisterAckFormatter(new Hl7SingleAckFormatter());
        return new EnhancedAckStrategy(registry, AckShape.Single);
    }
}
