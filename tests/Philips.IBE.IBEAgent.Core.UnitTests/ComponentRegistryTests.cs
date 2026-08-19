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

        var created = registry.CreateOutboundEndpoint(new OutputOptions { OutputId = 100 });

        Assert.Same(endpoint, created);
    }

    [Fact]
    public void CreateOutboundEndpoint_throws_for_unregistered_output_id()
    {
        var registry = new ComponentRegistry();

        Assert.Throws<InvalidOperationException>(() => registry.CreateOutboundEndpoint(new OutputOptions { OutputId = 1 }));
    }

    private sealed class FakeStage : IMessageStage
    {
        public Task<StageResult> ProcessAsync(MessageContext context) => Task.FromResult(StageResult.Continue);
    }
}
