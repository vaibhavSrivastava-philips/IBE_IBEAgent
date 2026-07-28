using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class SourceBasedRouterTests
{
    [Fact]
    public void Resolve_returns_the_contract_registered_for_the_source()
    {
        var registry = new ContractRegistry();
        var recording = new RecordingReplyContext();
        var endpoint = new FakeOutboundEndpoint();
        var leg = new DeliveryLeg(10, required: true, new BoundedInMemoryChannel(8), endpoint);
        var ingress = new Dictionary<int, IMessageChannel> { [1] = new BoundedInMemoryChannel(8) };
        var runtime = new ContractRuntime(ingress, new PassThroughPipeline(), new[] { leg });
        registry.Register(runtime, [1]);

        var router = new SourceBasedRouter(registry);
        var ctx = MessageContextBuilder.Create(sourceEndpointId: 1, reply: recording);

        Assert.Same(runtime, router.Resolve(ctx));
    }

    [Fact]
    public void Resolve_throws_when_source_is_not_routed_to_any_contract()
    {
        var registry = new ContractRegistry();
        var router = new SourceBasedRouter(registry);
        var ctx = MessageContextBuilder.Create(sourceEndpointId: 42);

        Assert.Throws<KeyNotFoundException>(() => router.Resolve(ctx));
    }
}
