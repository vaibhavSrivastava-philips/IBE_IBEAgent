using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// TEMPORARY placeholder stage — a no-op that passes every message straight through
// (StageResult.Continue), mutating neither payload nor headers. It exists so a catalog pipeline can
// name a registered stage end-to-end before the real stages (validate/filter/dedup/enrich, §3.6,
// Phase 5) are built. Remove once real stages land.
public sealed class PassThroughStage : IMessageStage
{
    public const string Name = "passthrough";

    public Task<StageResult> ProcessAsync(MessageContext context) => Task.FromResult(StageResult.Continue);
}
