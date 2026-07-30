using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// §3.6 — the SHARED pipeline: an ordered IMessageStage list that runs ONCE per message before fan-out.
// The PIPELINE drives the iteration and each stage RETURNS its decision (StageResult.Continue /
// StageResult.Filter(reason)), so a stage physically cannot "forget to continue" and silently skip the
// rest — that whole class of bug is designed away. A stage drops a message two ways:
//   1. return StageResult.Filter(reason)       -> routine filter/dedup drop (reason kept)
//   2. throw PipelineFilteredException(reason)  -> exceptional / hard-stop escape hatch
// Both surface as PipelineResult.Filtered(reason); the ContractRuntime then stops fan-out.
public sealed class MessagePipeline : IMessagePipeline
{
    private readonly IReadOnlyList<IMessageStage> _stages;

    public MessagePipeline(IReadOnlyList<IMessageStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        _stages = stages;
    }

    public ValueTask<PipelineResult> ExecuteAsync(MessageContext context)
    {
        // High-fidelity fast path: no stages -> identity, completes synchronously with ZERO Task
        // allocation. This is the ~80% (no-pipeline) case. A non-empty pipeline runs the async core;
        // an all-synchronous stage list also completes without allocating a Task (async ValueTask).
        if (_stages.Count == 0)
            return new ValueTask<PipelineResult>(PipelineResult.Continue);

        return ExecuteCoreAsync(context);
    }

    private async ValueTask<PipelineResult> ExecuteCoreAsync(MessageContext context)
    {
        foreach (var stage in _stages)
        {
            StageResult result;
            try
            {
                result = await stage.ProcessAsync(context).ConfigureAwait(false);
            }
            catch (PipelineFilteredException ex)
            {
                return PipelineResult.Filtered(ex.Reason);   // exceptional / hard-stop escape hatch
            }

            if (result.Filtered)
                return PipelineResult.Filtered(result.Reason);
        }

        return PipelineResult.Continue;
    }
}
