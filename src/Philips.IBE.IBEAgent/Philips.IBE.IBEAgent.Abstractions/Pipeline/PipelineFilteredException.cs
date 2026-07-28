namespace Philips.IBE.IBEAgent.Abstractions;

// A stage throws this to short-circuit the shared pipeline explicitly (§3.6), e.g. a filter/validation
// failure. MessagePipeline maps it to PipelineResult.Filtered(reason). Declared in Abstractions so any
// stage (including future Formats.* stages, which run outside Core) can throw it without depending on Core.
public sealed class PipelineFilteredException(string? reason = null) : Exception(reason)
{
    public string? Reason { get; } = reason;
}
