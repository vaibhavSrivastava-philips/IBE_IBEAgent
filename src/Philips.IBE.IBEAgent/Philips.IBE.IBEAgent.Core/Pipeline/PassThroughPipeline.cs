using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// Slice-1 shared pipeline: no stages, never short-circuits. The real stage-chaining
// pipeline (fold IMessageStage list + ParallelStage) arrives in Phase 5.
public sealed class PassThroughPipeline : IMessagePipeline
{
    public Task<PipelineResult> ExecuteAsync(MessageContext context)
        => Task.FromResult(PipelineResult.Continue);
}