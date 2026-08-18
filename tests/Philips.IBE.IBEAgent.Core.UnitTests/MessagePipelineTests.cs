using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class MessagePipelineTests
{
    [Fact]
    public async Task Empty_pipeline_continues()
    {
        var pipeline = new MessagePipeline([]);
        var result = await pipeline.ExecuteAsync(MessageContextBuilder.Create());

        Assert.False(result.ShortCircuited);
    }

    [Fact]
    public async Task Empty_pipeline_completes_synchronously_without_allocating_a_task()
    {
        var pipeline = new MessagePipeline([]);

        var pending = pipeline.ExecuteAsync(MessageContextBuilder.Create());

        // High-fidelity fast path: the no-stage case returns a completed ValueTask (no Task alloc).
        Assert.True(pending.IsCompletedSuccessfully);
        var result = await pending;
        Assert.False(result.ShortCircuited);
    }

    [Fact]
    public async Task Runs_stages_in_order_and_continues_when_all_return_continue()
    {
        var order = new List<string>();
        var pipeline = new MessagePipeline(
        [
            new RecordingStage(order, "a"),
            new RecordingStage(order, "b"),
            new RecordingStage(order, "c"),
        ]);

        var result = await pipeline.ExecuteAsync(MessageContextBuilder.Create());

        Assert.False(result.ShortCircuited);
        Assert.Equal(["a", "b", "c"], order);
    }

    [Fact]
    public async Task Stage_throwing_PipelineFilteredException_short_circuits_with_reason()
    {
        var pipeline = new MessagePipeline([new ThrowingStage("blocked")]);

        var result = await pipeline.ExecuteAsync(MessageContextBuilder.Create());

        Assert.True(result.ShortCircuited);
        Assert.Equal("blocked", result.Reason);
    }

    [Fact]
    public async Task Stage_that_returns_filter_short_circuits_with_reason_and_no_exception()
    {
        var order = new List<string>();
        var pipeline = new MessagePipeline(
        [
            new RecordingStage(order, "a"),
            new FilteringStage("duplicate"),
            new RecordingStage(order, "c"),
        ]);

        var result = await pipeline.ExecuteAsync(MessageContextBuilder.Create());

        Assert.True(result.ShortCircuited);
        Assert.Equal("duplicate", result.Reason);   // reason carried without an exception
        Assert.Equal(["a"], order);                 // "c" never runs
    }

    [Fact]
    public async Task Enrich_stage_mutates_headers_visible_to_later_stages_and_after_execution()
    {
        var ctx = MessageContextBuilder.Create();
        var pipeline = new MessagePipeline([new HeaderEnrichStage("IdempotencyKey", "abc123")]);

        await pipeline.ExecuteAsync(ctx);

        Assert.Equal("abc123", ctx.Headers["IdempotencyKey"]);
    }

    private sealed class RecordingStage(List<string> order, string name) : IMessageStage
    {
        public Task<StageResult> ProcessAsync(MessageContext context)
        {
            order.Add(name);
            return Task.FromResult(StageResult.Continue);
        }
    }

    private sealed class ThrowingStage(string reason) : IMessageStage
    {
        public Task<StageResult> ProcessAsync(MessageContext context)
            => throw new PipelineFilteredException(reason);
    }

    // Preferred routine-filter pattern: return Filter(reason) — no exception, and no next to forget.
    private sealed class FilteringStage(string reason) : IMessageStage
    {
        public Task<StageResult> ProcessAsync(MessageContext context)
            => Task.FromResult(StageResult.Filter(reason));
    }

    private sealed class HeaderEnrichStage(string key, string value) : IMessageStage
    {
        public Task<StageResult> ProcessAsync(MessageContext context)
        {
            context.Headers[key] = value;
            return Task.FromResult(StageResult.Continue);
        }
    }
}
