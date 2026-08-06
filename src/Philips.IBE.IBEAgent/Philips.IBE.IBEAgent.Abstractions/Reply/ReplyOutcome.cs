namespace Philips.IBE.IBEAgent.Abstractions;

// §6 — the settled reply for ONE received message, handed to the IAckStrategy. Carries the overall
// outcome, an optional low-cardinality reason (failure/filter), and the per-required-leg results
// (ordered by OutputId) that Enhanced ack combines: Single relays one, Batch (future) wraps all.
// LegResults is empty for the receipt (Normal) and filtered cases.
public readonly record struct ReplyOutcome(
    DeliveryOutcome Outcome,
    string? Reason,
    IReadOnlyList<DeliveryResult> LegResults)
{
    private static readonly IReadOnlyList<DeliveryResult> NoLegs = [];

    public static ReplyOutcome Received() => new(DeliveryOutcome.Accepted, null, NoLegs);
    public static ReplyOutcome Delivered(IReadOnlyList<DeliveryResult> legResults) => new(DeliveryOutcome.Delivered, null, legResults);
    public static ReplyOutcome Failed(string? reason, IReadOnlyList<DeliveryResult> legResults) => new(DeliveryOutcome.Failed, reason, legResults);
    public static ReplyOutcome Filtered(string? reason) => new(DeliveryOutcome.Filtered, reason, NoLegs);
}
