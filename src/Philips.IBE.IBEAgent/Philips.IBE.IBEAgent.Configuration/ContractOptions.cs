namespace Philips.IBE.IBEAgent.Configuration;

// §8 — Contract = N Inputs -> M Outputs + one shared Pipeline (by name) + one reply mode
// (Acknowledgement XOR Response). Config declares topology + limits; code defines behavior (P8).
public sealed record ContractOptions
{
    public required string Name { get; init; }
    public required IReadOnlyList<InputOptions> Inputs { get; init; }
    public IReadOnlyList<int>? InputIds { get; init; }          // backward-compat shorthand -> Inputs w/ default Channel
    public AckOptions Acknowledgement { get; init; } = new();
    public ResponseOptions Response { get; init; } = new();
    public string? Pipeline { get; init; }                      // catalog Pipelines entry name; null = no processing stages
    public required IReadOnlyList<OutputOptions> Outputs { get; init; }
}
