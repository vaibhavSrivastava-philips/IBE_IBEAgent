namespace Philips.IBE.IBEAgent.Abstractions;

// §3.6 — what a shared-pipeline stage returns to the pipeline: continue to the next stage, or FILTER
// (drop the message for ALL outputs) with an optional low-cardinality reason for observability.
// A value-returning contract (rather than a `next` delegate the stage must remember to call) makes
// "silently skip the rest of the pipeline" impossible to express: a stage MUST return Continue or
// Filter (or throw). The reason flows to PipelineResult.Filtered(reason).
public readonly record struct StageResult(bool Filtered, string? Reason = null)
{
    public static readonly StageResult Continue = new(false);

    public static StageResult Filter(string? reason = null) => new(true, reason);
}
