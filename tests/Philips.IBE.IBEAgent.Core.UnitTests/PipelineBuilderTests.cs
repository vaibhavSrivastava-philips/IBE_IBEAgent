using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;
using Philips.IBE.IBEAgent.Core;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class PipelineBuilderTests
{
    [Fact]
    public void Build_returns_empty_pipeline_when_name_is_null()
    {
        var pipeline = PipelineBuilder.Build(null, new CatalogOptions(), new ComponentRegistry());

        Assert.NotNull(pipeline);
    }

    [Fact]
    public void Build_throws_for_unknown_pipeline_name()
    {
        Assert.Throws<InvalidOperationException>(
            () => PipelineBuilder.Build("missing", new CatalogOptions(), new ComponentRegistry()));
    }

    [Fact]
    public async Task Build_resolves_named_stages_in_order()
    {
        var order = new List<string>();
        var registry = new ComponentRegistry()
            .RegisterStage("a", () => new RecordingStage("a", order))
            .RegisterStage("b", () => new RecordingStage("b", order));
        var catalog = new CatalogOptions
        {
            Pipelines = new Dictionary<string, IReadOnlyList<string>> { ["main"] = ["a", "b"] },
        };

        var pipeline = PipelineBuilder.Build("main", catalog, registry);
        var ctx = MakeContext();
        await pipeline.ExecuteAsync(ctx);

        Assert.Equal(["a", "b"], order);
    }

    private static MessageContext MakeContext() =>
        new("corr-1", 1, "hl7v2", new NoopAckToken(), new NoopReplyContext());

    private sealed class RecordingStage(string name, List<string> order) : IMessageStage
    {
        public Task<StageResult> ProcessAsync(MessageContext context)
        {
            lock (order) order.Add(name);
            return Task.FromResult(StageResult.Continue);
        }
    }

    private sealed class NoopAckToken : IAckToken
    {
        public Task WriteAsync(ReadOnlyMemory<byte> reply, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopReplyContext : IReplyContext
    {
        public void Attach(MessageContext message) { }
        public void OnFannedOut(int requiredTotal) { }
        public void ReportFiltered(string? reason = null) { }
        public void ReportLeg(bool required, in DeliveryResult result) { }
    }
}
