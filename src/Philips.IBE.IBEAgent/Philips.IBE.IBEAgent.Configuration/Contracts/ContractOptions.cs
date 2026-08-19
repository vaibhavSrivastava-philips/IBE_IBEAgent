using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Configuration;

// §8 — Contract = N Inputs -> M Outputs + one shared Pipeline (by name) + one reply mode
// (Acknowledgement XOR Response). Config declares topology + limits; code defines behavior (P8).
public sealed record ContractOptions
{
    public required string Name { get; init; }
    public required IReadOnlyList<InputOptions> Inputs { get; init; }
    public IReadOnlyList<int>? InputIds { get; init; }          // backward-compat shorthand -> Inputs w/ default Channel
    public WorkflowRef? Workflow { get; init; }                 // references a catalog Workflows entry (Use) + the FSE's Settings bag; supplies shared Pipeline + default per-leg Format
    public AckOptions Acknowledgement { get; init; } = new();
    public ResponseOptions Response { get; init; } = new();
    public bool? ReplyOnFilter { get; init; }                   // resolved reply-on-filter (a Workflow Setting binds here; a direct value is the manual/legacy path). null -> false.
    public string? Pipeline { get; init; }                      // manual/legacy override; used only when Workflow is not set. null = no processing stages
    public required IReadOnlyList<OutputOptions> Outputs { get; init; }
    public IReadOnlyDictionary<string, StageParameters>? StageParameterSets { get; init; }   // RESOLVED: per-stage parameters bound from Workflow Settings (stage: targets). Not authored directly.
}
