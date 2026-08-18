using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// Production no-op stage: explicitly allows a catalog pipeline to opt into pass-through behavior while
// still exercising the same pipeline registration/execution path as filtering or enrichment stages.
public sealed class PassThroughStage : IMessageStage
{
    public const string Name = "passthrough";

    public Task<StageResult> ProcessAsync(MessageContext context) => Task.FromResult(StageResult.Continue);
}
