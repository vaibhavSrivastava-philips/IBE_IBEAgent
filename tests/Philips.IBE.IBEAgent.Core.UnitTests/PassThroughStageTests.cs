using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class PassThroughStageTests
{
    [Fact]
    public async Task ProcessAsync_returns_continue()
    {
        var result = await new PassThroughStage().ProcessAsync(MessageContextBuilder.Create());

        Assert.False(result.Filtered);
    }

    [Fact]
    public async Task ProcessAsync_does_not_mutate_payload_or_headers()
    {
        var ctx = MessageContextBuilder.Create(payload: "MSH|^~\\&|A|B");
        var payloadBefore = ctx.Payload;
        ctx.Headers["existing"] = "value";
        var headerCountBefore = ctx.Headers.Count;

        await new PassThroughStage().ProcessAsync(ctx);

        Assert.True(ctx.Payload.Span.SequenceEqual(payloadBefore.Span));
        Assert.Equal(headerCountBefore, ctx.Headers.Count);
    }
}
