namespace Philips.IBE.IBEAgent.Configuration;

// §8 / ADR 0001 (D2-D3) — an FSE contract's reference to a developer-owned Workflow. Always an object:
//   "Workflow": { "Use": "adt", "Settings": { "AckTimeoutSeconds": 45 } }
// The zero-config case is just { "Use": "adt" } (no Settings). Settings is a flat, friendly key:value
// bag the Workflow author exposed; the resolver validates each value against the Workflow's declared
// Setting definitions and binds it onto the contract. FSEs never see field paths, stage names, or modes.
public sealed record WorkflowRef
{
    public string? Use { get; init; }                                    // names a catalog Workflows entry
    public IReadOnlyDictionary<string, string?>? Settings { get; init; } // FSE-supplied values, keyed by friendly Setting name
}
