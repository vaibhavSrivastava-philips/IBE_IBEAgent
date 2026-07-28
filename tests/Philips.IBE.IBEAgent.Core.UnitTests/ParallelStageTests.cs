using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class ParallelStageTests
{
    [Fact]
    public async Task Runs_all_branches_and_calls_next()
    {
        var ctx = MessageContextBuilder.Create();
        var nextCalled = false;

        var stage = new ParallelStage(
        [
            [new HeaderStage("branch1", "done")],
            [new HeaderStage("branch2", "done")],
        ]);

        await stage.InvokeAsync(ctx, _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.True(nextCalled);
        Assert.Equal("done", ctx.Headers["branch1"]);
        Assert.Equal("done", ctx.Headers["branch2"]);
    }

    [Fact]
    public async Task No_branches_still_calls_next()
    {
        var stage = new ParallelStage([]);
        var nextCalled = false;

        await stage.InvokeAsync(MessageContextBuilder.Create(), _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Branch_is_itself_a_sequential_sub_pipeline()
    {
        var order = new List<string>();
        var ctx = MessageContextBuilder.Create();

        var stage = new ParallelStage(
        [
            [new OrderedStage(order, "b1-first"), new OrderedStage(order, "b1-second")],
        ]);

        await stage.InvokeAsync(ctx, _ => Task.CompletedTask);

        Assert.Equal(["b1-first", "b1-second"], order);
    }

    [Fact]
    public async Task Faulting_branch_propagates_failFast()
    {
        var stage = new ParallelStage(
        [
            [new ThrowingStage()],
        ]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => stage.InvokeAsync(MessageContextBuilder.Create(), _ => Task.CompletedTask));
    }

    [Fact]
    public async Task Composes_inside_a_MessagePipeline_as_one_ordinary_stage()
    {
        var ctx = MessageContextBuilder.Create();
        var pipeline = new MessagePipeline(
        [
            new ParallelStage(
            [
                [new HeaderStage("a", "1")],
                [new HeaderStage("b", "2")],
            ]),
        ]);

        var result = await pipeline.ExecuteAsync(ctx);

        Assert.False(result.ShortCircuited);
        Assert.Equal("1", ctx.Headers["a"]);
        Assert.Equal("2", ctx.Headers["b"]);
    }

    private sealed class HeaderStage(string key, string value) : IMessageStage
    {
        public Task InvokeAsync(MessageContext context, StageDelegate next)
        {
            context.Headers[key] = value;
            return next(context);
        }
    }

    private sealed class OrderedStage(List<string> order, string name) : IMessageStage
    {
        public Task InvokeAsync(MessageContext context, StageDelegate next)
        {
            lock (order) order.Add(name);
            return next(context);
        }
    }

    private sealed class ThrowingStage : IMessageStage
    {
        public Task InvokeAsync(MessageContext context, StageDelegate next)
            => throw new InvalidOperationException("branch failed");
    }
}
