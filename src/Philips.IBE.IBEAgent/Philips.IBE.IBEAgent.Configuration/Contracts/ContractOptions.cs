namespace Philips.IBE.IBEAgent.Configuration;

// §8 — Contract = N Inputs -> M Outputs + one shared Pipeline (by name) + one reply mode
// (Acknowledgement XOR Response). Config declares topology + limits; code defines behavior (P8).
public sealed record ContractOptions
{
    public required string Name { get; init; }
    public required IReadOnlyList<InputOptions> Inputs { get; init; }
    public IReadOnlyList<int>? InputIds { get; init; }          // backward-compat shorthand -> Inputs w/ default Channel
    public string? Template { get; init; }                      // catalog Templates entry name; supplies shared Pipeline + default per-leg Format
    public AckOptions Acknowledgement { get; init; } = new();
    public ResponseOptions Response { get; init; } = new();
    public bool? ReplyOnFilter { get; init; }                   // OPTIONAL override of the Template's default. null = inherit (dev decides in the catalog Template); true = reply with an intentional reject, false = silent drop
    public string? Pipeline { get; init; }                      // manual/legacy override; used only when Template is not set. null = no processing stages
    public required IReadOnlyList<OutputOptions> Outputs { get; init; }
}
