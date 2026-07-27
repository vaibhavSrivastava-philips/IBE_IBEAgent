using System.Text;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class NormalAckStrategyTests
{
    [Fact]
    public void Replies_on_receipt() => Assert.True(new NormalAckStrategy().RepliesOnReceipt);

    [Fact]
    public async Task Writes_stub_ack_via_token()
    {
        var token = new FakeAckToken();
        var ctx = MessageContextBuilder.Create(ack: token);

        await new NormalAckStrategy().WriteReplyAsync(ctx, new DeliveryResult(DeliveryOutcome.Accepted));

        Assert.Equal(1, token.WriteCount);
        Assert.Equal("MSA|AA|received", Encoding.UTF8.GetString(token.Writes[0]));
    }
}
