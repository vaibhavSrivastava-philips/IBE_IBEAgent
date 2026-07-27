using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class ReplyContextFactoryTests
{
    [Fact]
    public void Created_context_replies_on_receipt_via_the_message_token()
    {
        var token = new FakeAckToken();
        var factory = new ReplyContextFactory(new NormalAckStrategy());

        var reply = factory.Create(sourceEndpointId: 1, ackToken: token);
        var ctx = MessageContextBuilder.Create(ack: token, reply: reply);
        reply.Attach(ctx);

        reply.OnFannedOut(1);

        Assert.Equal(1, token.WriteCount);
    }
}
