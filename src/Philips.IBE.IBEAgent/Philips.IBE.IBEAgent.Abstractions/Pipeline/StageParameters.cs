namespace Philips.IBE.IBEAgent.Abstractions;

// §3.10 / ADR 0001 D8 — construction-time parameters handed to a stage when the pipeline is compiled
// (mirrors how a codec receives CodecOptions). A flat name -> value bag, empty for a no-param stage; a
// stage reads only the keys it knows. Values are sourced from the contract's Workflow Settings (the
// "stage:<name>.<key>" Bind targets), so a dev stage can require an FSE-supplied resource or option.
public sealed record StageParameters
{
    public static readonly StageParameters None = new();

    public IReadOnlyDictionary<string, string?> Values { get; init; } =
        new Dictionary<string, string?>(StringComparer.Ordinal);

    public string? Get(string key) => Values.TryGetValue(key, out var value) ? value : null;
}
