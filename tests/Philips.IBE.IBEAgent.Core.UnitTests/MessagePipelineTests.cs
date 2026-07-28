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
    public async Task Runs_stages_in_order_and_continues_when_all_call_next()
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
    public async Task Stage_that_does_not_call_next_short_circuits()
    {
        var order = new List<string>();
        var pipeline = new MessagePipeline(
        [
            new RecordingStage(order, "a"),
            new SwallowingStage(),
            new RecordingStage(order, "c"),
        ]);

        var result = await pipeline.ExecuteAsync(MessageContextBuilder.Create());

        Assert.True(result.ShortCircuited);
        Assert.Equal(["a"], order);   // "c" never runs
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
    public async Task Enrich_stage_mutates_headers_visible_to_later_stages_and_after_execution()
    {
        var ctx = MessageContextBuilder.Create();
        var pipeline = new MessagePipeline([new HeaderEnrichStage("IdempotencyKey", "abc123")]);

        await pipeline.ExecuteAsync(ctx);

        Assert.Equal("abc123", ctx.Headers["IdempotencyKey"]);
    }

    private sealed class RecordingStage(List<string> order, string name) : IMessageStage
    {
        public Task InvokeAsync(MessageContext context, StageDelegate next)
        {
            order.Add(name);
            return next(context);
        }
    }

    private sealed class SwallowingStage : IMessageStage
    {
        public Task InvokeAsync(MessageContext context, StageDelegate next) => Task.CompletedTask; // never calls next
    }

    private sealed class ThrowingStage(string reason) : IMessageStage
    {
        public Task InvokeAsync(MessageContext context, StageDelegate next)
            => throw new PipelineFilteredException(reason);
    }

    private sealed class HeaderEnrichStage(string key, string value) : IMessageStage
    {
        public Task InvokeAsync(MessageContext context, StageDelegate next)
        {
            context.Headers[key] = value;
            return next(context);
        }
    }
}
