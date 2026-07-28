using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// §3.6a — Scatter-Gather (EIP): a composite IMessageStage holding N branches (each branch is itself a
// sub-chain of IMessageStage). Runs branches concurrently against the SAME context, then joins before
// calling `next`. To the outer pipeline it looks like one ordinary stage (Composite pattern).
//
// Correctness rules (mandatory, per architecture §3.6a):
//   - Join policy today is `all` (Task.WhenAll); error policy is `failFast` (first exception propagates,
//     which the outer MessagePipeline treats as an unfiltered fault — same as any other stage exception).
//   - Branches are expected to enrich headers/metadata; at most one branch should transform the payload.
//     Because IMessageStage stages mutate the shared, mutable-during-pipeline MessageContext (A5)
//     directly, callers are responsible for keeping branches side-effect-disjoint (§3.6a note on
//     "results-out, not concurrent mutation"). This composite does not defensively clone per branch —
//     that is a deferred perf/isolation option (P10), not required for correctness of additive metadata.
public sealed class ParallelStage : IMessageStage
{
    private readonly IReadOnlyList<IReadOnlyList<IMessageStage>> _branches;

    public ParallelStage(IReadOnlyList<IReadOnlyList<IMessageStage>> branches)
    {
        ArgumentNullException.ThrowIfNull(branches);
        _branches = branches;
    }

    public async Task InvokeAsync(MessageContext context, StageDelegate next)
    {
        if (_branches.Count > 0)
        {
            await Task.WhenAll(_branches.Select(branch => RunBranchAsync(branch, context))).ConfigureAwait(false);
        }

        await next(context).ConfigureAwait(false);
    }

    private static Task RunBranchAsync(IReadOnlyList<IMessageStage> branch, MessageContext context)
    {
        // A branch is itself a sequential mini-pipeline; its own terminal delegate is a no-op —
        // the join for a branch is simply that its chain completed.
        StageDelegate chain = static _ => Task.CompletedTask;
        for (var i = branch.Count - 1; i >= 0; i--)
        {
            var stage = branch[i];
            var localNext = chain;
            chain = ctx => stage.InvokeAsync(ctx, localNext);
        }

        return chain(context);
    }
}
