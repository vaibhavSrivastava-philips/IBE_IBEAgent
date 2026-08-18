using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class ReplyContextFactoryTests
{
    private static ReplyPolicy NormalPolicy()
        => new(new NormalAckStrategy(new ComponentRegistry(), AckShape.Single), Timeout.InfiniteTimeSpan, ReplyOnFilter: false);

    [Fact]
    public void Create_resolves_the_registered_source_policy_and_replies_on_receipt()
    {
        var token = new FakeAckToken();
        var factory = new ReplyContextFactory(new Dictionary<int, ReplyPolicy> { [1] = NormalPolicy() });

        var reply = factory.Create(sourceEndpointId: 1, ackToken: token);
        var ctx = MessageContextBuilder.Create(ack: token, reply: reply);
        reply.Attach(ctx);

        reply.OnFannedOut(1);

        Assert.Equal(1, token.WriteCount);
    }

    [Fact]
    public void Create_throws_when_the_source_has_no_registered_policy()
    {
        var factory = new ReplyContextFactory(new Dictionary<int, ReplyPolicy> { [1] = NormalPolicy() });

        Assert.Throws<KeyNotFoundException>(() => factory.Create(sourceEndpointId: 99, ackToken: new FakeAckToken()));
    }
}
