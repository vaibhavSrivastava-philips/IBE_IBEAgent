namespace Philips.IBE.IBEAgent.Abstractions;

// result of running the shared pipeline once. ShortCircuited => filtered/invalid for ALL outputs.
public readonly record struct PipelineResult(
    bool ShortCircuited,
    DeliveryOutcome Outcome = DeliveryOutcome.Accepted,
    string? Reason = null)
{
    public static PipelineResult Continue { get; } = new(false);
    public static PipelineResult Filtered(string? reason = null)
        => new(true, DeliveryOutcome.Filtered, reason);
}