namespace Philips.IBE.IBEAgent.Formats.Hl7.Filtering;

public sealed record Hl7FilterOptions
{
    public IReadOnlyList<string> AllowedMessageTypes { get; init; } = [];
    public IReadOnlyList<string> BlockedMessageTypes { get; init; } = [];
    public IReadOnlyList<Hl7FieldFilterRule> FieldRules { get; init; } = [];
}

public sealed record Hl7FieldFilterRule
{
    public required string Segment { get; init; }
    public required int Field { get; init; }
    public int Occurrence { get; init; }
    public string? EqualsValue { get; init; }
    public string? NotEqualsValue { get; init; }
    public string? Reason { get; init; }
}
