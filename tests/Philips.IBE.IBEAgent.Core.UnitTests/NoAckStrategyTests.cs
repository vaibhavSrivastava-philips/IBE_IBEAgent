using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

// §6/§8 — proves the "no ack" (fire-and-forget) reply mode: no bytes are ever written back to the
// source's IAckToken, regardless of whether the message is filtered, delivered, or fails delivery.
public sealed class NoAckStrategyTests
{
    [Fact]
    public void Replies_on_receipt_so_fan_out_settles_immediately()
        => Assert.True(new NoAckStrategy().RepliesOnReceipt);

    [Fact]
    public async Task Writes_nothing_on_accepted()
    {
        var token = new FakeAckToken();
        var ctx = MessageContextBuilder.Create(ack: token);

        await new NoAckStrategy().WriteReplyAsync(ctx, ReplyOutcome.Received());

        Assert.Equal(0, token.WriteCount);
    }

    [Fact]
    public async Task Writes_nothing_on_delivery_failure()
    {
        var token = new FakeAckToken();
        var ctx = MessageContextBuilder.Create(ack: token);

        await new NoAckStrategy().WriteReplyAsync(ctx, ReplyOutcome.Failed("boom", []));

        Assert.Equal(0, token.WriteCount);
    }
}
