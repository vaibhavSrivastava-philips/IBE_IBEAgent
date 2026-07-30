namespace Philips.IBE.IBEAgent.Abstractions;

// §3.6 — thrown to short-circuit the shared pipeline for an EXCEPTIONAL / hard stop (a fault a stage
// can't handle locally). MessagePipeline maps it to PipelineResult.Filtered(reason). For ROUTINE,
// filter/dedup drops prefer returning `StageResult.Filter(reason)` — cheaper than an exception per drop.
// Declared in Abstractions so any stage (including future Formats.* stages that run outside Core) can
// throw it without depending on Core.
public sealed class PipelineFilteredException(string? reason = null) : Exception(reason)
{
    public string? Reason { get; } = reason;
}
