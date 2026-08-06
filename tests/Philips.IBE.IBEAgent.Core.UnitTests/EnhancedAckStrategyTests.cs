using System.Text;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

// §6 — Enhanced ack (Single shape): relays one required leg's captured ack on success (the first by
// OutputId, already ordered by ReplyContext), synthesizes a positive ack when a delivered leg brought
// no bytes, and generates one NACK on failure (all-required rule).
public sealed class EnhancedAckStrategyTests
{
    private static EnhancedAckStrategy Create(ComponentRegistry? registry = null)
        => new(registry ?? new ComponentRegistry(), AckShape.Single);

    [Fact]
    public void Waits_for_delivery() => Assert.False(Create().RepliesOnReceipt);

    [Fact]
    public async Task Relays_the_single_leg_ack_on_success()
    {
        var token = new FakeAckToken();
        var ctx = MessageContextBuilder.Create(ack: token);
        DeliveryResult[] legs = [new(DeliveryOutcome.Delivered, ResponsePayload: Encoding.UTF8.GetBytes("ACK-100"))];

        await Create().WriteReplyAsync(ctx, ReplyOutcome.Delivered(legs));

        Assert.Equal("ACK-100", Encoding.UTF8.GetString(token.Writes[0]));
    }

    [Fact]
    public async Task Relays_the_first_leg_ack_on_multi_output_success()
    {
        var token = new FakeAckToken();
        var ctx = MessageContextBuilder.Create(ack: token);
        // ReplyContext hands results ordered by OutputId; the strategy relays the first with bytes.
        DeliveryResult[] legs =
        [
            new(DeliveryOutcome.Delivered, ResponsePayload: Encoding.UTF8.GetBytes("ACK-A")),
            new(DeliveryOutcome.Delivered, ResponsePayload: Encoding.UTF8.GetBytes("ACK-B")),
        ];

        await Create().WriteReplyAsync(ctx, ReplyOutcome.Delivered(legs));

        Assert.Equal("ACK-A", Encoding.UTF8.GetString(token.Writes[0]));
    }

    [Fact]
    public async Task Generates_positive_ack_when_delivered_without_captured_bytes()
    {
        var token = new FakeAckToken();
        var ctx = MessageContextBuilder.Create(ack: token);
        DeliveryResult[] legs = [new(DeliveryOutcome.Delivered)];   // delivered but no response bytes

        await Create().WriteReplyAsync(ctx, ReplyOutcome.Delivered(legs));

        Assert.Equal("IBE:ACK (no ack formatter)", Encoding.UTF8.GetString(token.Writes[0]));
    }

    [Fact]
    public async Task Generates_one_nack_on_failure()
    {
        var token = new FakeAckToken();
        var ctx = MessageContextBuilder.Create(ack: token);

        await Create().WriteReplyAsync(ctx, ReplyOutcome.Failed("C down", []));

        Assert.Equal(1, token.WriteCount);
        Assert.Equal("IBE:NACK (no ack formatter)", Encoding.UTF8.GetString(token.Writes[0]));   // fallback: no formatter registered
    }
}
