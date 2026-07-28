namespace Philips.IBE.IBEAgent.Configuration;

// §3.6a — a catalog Pipelines entry may nest a `parallel` composite: each Branch is itself an
// ordered list of stage names (a sequential sub-pipeline); branches run concurrently (§3.6a).
public sealed record ParallelStageOptions
{
    public const string TypeName = "parallel";

    public string Type { get; init; } = TypeName;
    public string Join { get; init; } = "all";        // only "all" (WhenAll) is supported today
    public string OnError { get; init; } = "failFast"; // only "failFast" is supported today
    public required IReadOnlyList<IReadOnlyList<string>> Branches { get; init; }
}
