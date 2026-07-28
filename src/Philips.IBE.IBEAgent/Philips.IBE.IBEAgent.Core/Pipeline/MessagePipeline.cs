using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// §3.6 — the SHARED pipeline: an IMessageStage chain (Chain-of-Responsibility / middleware) that runs
// ONCE per message before fan-out. Builds the chain once (constructor) and replays it per message.
// A stage short-circuits by either not calling `next` or by throwing PipelineFilteredException;
// both surface as PipelineResult.Filtered(reason) — the ContractRuntime stops fan-out for that message.
public sealed class MessagePipeline : IMessagePipeline
{
    private readonly IReadOnlyList<IMessageStage> _stages;

    public MessagePipeline(IReadOnlyList<IMessageStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        _stages = stages;
    }

    public async Task<PipelineResult> ExecuteAsync(MessageContext context)
    {
        var reachedTerminal = false;

        // Build inside-out per invocation so each stage can safely capture per-message state;
        // the chain itself is cheap (a handful of delegate allocations) — correctness over micro-opt.
        StageDelegate chain = _ => { reachedTerminal = true; return Task.CompletedTask; };
        for (var i = _stages.Count - 1; i >= 0; i--)
        {
            var stage = _stages[i];
            var next = chain;
            chain = ctx => stage.InvokeAsync(ctx, next);
        }

        try
        {
            await chain(context).ConfigureAwait(false);
        }
        catch (PipelineFilteredException ex)
        {
            return PipelineResult.Filtered(ex.Reason);
        }

        return reachedTerminal ? PipelineResult.Continue : PipelineResult.Filtered();
    }
}
