using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class ComponentRegistryTests
{
    [Fact]
    public void CreateStage_returns_registered_instance()
    {
        var registry = new ComponentRegistry().RegisterStage("noop", _ => new FakeStage());

        var stage = registry.CreateStage("noop", StageParameters.None);

        Assert.IsType<FakeStage>(stage);
    }

    [Fact]
    public void CreateStage_throws_for_unknown_name()
    {
        var registry = new ComponentRegistry();

        Assert.Throws<InvalidOperationException>(() => registry.CreateStage("missing", StageParameters.None));
    }

    [Fact]
    public void CreateMessageCodec_resolves_by_type()
    {
        var registry = new ComponentRegistry().RegisterMessageCodec("hl7v2", _ => new FakeMessageCodec());

        var codec = registry.CreateMessageCodec("hl7v2", new CodecOptions { Type = "hl7v2" });

        Assert.IsType<FakeMessageCodec>(codec);
    }

    [Fact]
    public void CreateOutboundEndpoint_resolves_by_output_id()
    {
        var endpoint = new FakeOutboundEndpoint();
        var registry = new ComponentRegistry().RegisterOutboundEndpoint(100, _ => endpoint);

        // MaxAttempts=1 leaves the endpoint un-decorated so this asserts pure resolution; retry wrapping is covered separately.
        var created = registry.CreateOutboundEndpoint(new OutputOptions { OutputId = 100, Retry = new RetryOptions { MaxAttempts = 1 } });

        Assert.Same(endpoint, created);
    }

    [Fact]
    public void CreateOutboundEndpoint_throws_for_unregistered_output_id()
    {
        var registry = new ComponentRegistry();

        Assert.Throws<InvalidOperationException>(() => registry.CreateOutboundEndpoint(new OutputOptions { OutputId = 1 }));
    }

    [Fact]
    public async Task CreateOutboundEndpoint_wraps_with_retry_when_MaxAttempts_exceeds_one()
    {
        var calls = 0;
        var inner = new FakeOutboundEndpoint(_ => ++calls < 2
            ? new DeliveryResult(DeliveryOutcome.Failed, "transient")
            : new DeliveryResult(DeliveryOutcome.Delivered));
        var registry = new ComponentRegistry().RegisterOutboundEndpoint(100, _ => inner);

        var created = registry.CreateOutboundEndpoint(new OutputOptions
        {
            OutputId = 100,
            Retry = new RetryOptions { MaxAttempts = 2, BackoffSeconds = 0 },
        });
        var result = await created.SendAsync(MessageContextBuilder.Create(), CancellationToken.None);

        Assert.NotSame(inner, created);
        Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void CreateOutboundEndpoint_keeps_the_bare_endpoint_lifecycle_when_wrapped()
    {
        var inner = new LifecycleEndpoint();
        var registry = new ComponentRegistry().RegisterOutboundEndpoint(100, _ => inner);

        var created = registry.CreateOutboundEndpoint(new OutputOptions { OutputId = 100, Retry = new RetryOptions { MaxAttempts = 3 } });

        Assert.NotSame(inner, created);                                          // delivery path is decorated
        Assert.Same(inner, Assert.Single(registry.OutboundEndpointLifecycles));  // bare endpoint still tracked for lifecycle
    }

    private sealed class FakeStage : IMessageStage
    {
        public Task<StageResult> ProcessAsync(MessageContext context) => Task.FromResult(StageResult.Continue);
    }

    private sealed class LifecycleEndpoint : IOutboundEndpoint, IEndpointLifecycle
    {
        public Task<DeliveryResult> SendAsync(MessageContext context, CancellationToken cancellationToken)
            => Task.FromResult(new DeliveryResult(DeliveryOutcome.Delivered));
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
