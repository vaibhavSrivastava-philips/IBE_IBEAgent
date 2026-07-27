using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class PassThroughPipelineTests
{
    [Fact]
    public async Task Never_short_circuits()
    {
        var result = await new PassThroughPipeline().ExecuteAsync(MessageContextBuilder.Create());
        Assert.False(result.ShortCircuited);
    }
}
