using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class DispatcherTests
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task DispatchAsync_routes_to_the_resolved_contract_and_enqueues()
    {
        var recording = new RecordingReplyContext();
        var endpoint = new FakeOutboundEndpoint();
        var leg = new DeliveryLeg(10, required: true, new BoundedInMemoryChannel(8), endpoint);
        var ingress = new Dictionary<int, IMessageChannel> { [1] = new BoundedInMemoryChannel(8) };
        var runtime = new ContractRuntime(ingress, new MessagePipeline([]), new[] { leg });

        var registry = new ContractRegistry();
        registry.Register(runtime, [1]);
        var router = new SourceBasedRouter(registry);
        var dispatcher = new Dispatcher(router);

        _ = runtime.RunAsync(CancellationToken.None);
        var ctx = MessageContextBuilder.Create(sourceEndpointId: 1, reply: recording);

        await dispatcher.DispatchAsync(ctx, CancellationToken.None);
        await runtime.DrainAsync(DrainTimeout);

        Assert.Single(endpoint.Sent);
        Assert.Equal(1, recording.ArmedRequiredTotal);
    }

    [Fact]
    public async Task DispatchAsync_throws_when_no_contract_matches_the_source()
    {
        var registry = new ContractRegistry();
        var router = new SourceBasedRouter(registry);
        var dispatcher = new Dispatcher(router);
        var ctx = MessageContextBuilder.Create(sourceEndpointId: 99);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => dispatcher.DispatchAsync(ctx, CancellationToken.None));
    }
}
