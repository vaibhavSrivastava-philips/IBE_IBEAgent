namespace Philips.IBE.IBEAgent.Configuration;

// §3.3 (ADR 0001) — one delegated knob a Workflow author exposes to the FSE. A friendly key maps to a
// hidden Bind target (a contract field path, or a stage parameter). Guardrails (Min/Max/Allowed/Regex)
// are enforced and Scale is applied by the resolver. A null Default makes the setting required (the FSE
// must supply a value); a present Default makes it optional. Anything NOT declared as a Setting is
// constant and invisible to the FSE.
public sealed record SettingDefinition
{
    public string? Description { get; init; }
    public string? Default { get; init; }                 // null = required (the FSE must supply a value)
    public double? Min { get; init; }                     // numeric lower bound (inclusive)
    public double? Max { get; init; }                     // numeric upper bound (inclusive)
    public IReadOnlyList<string>? Allowed { get; init; }  // exact allow-list (a "choice" setting)
    public string? Regex { get; init; }                   // pattern the value must match
    public string? Bind { get; init; }                    // target path: e.g. Acknowledgement.TimeoutMs | Outputs[].Retry.MaxAttempts | stage:hl7-filter.Ruleset. null = the key IS the field name.
    public string? Kind { get; init; }                    // file | secret | null (scalar). file -> security-checked path (confined to the resources root); secret -> secret store.
    public string? ContentType { get; init; }             // for Kind:file: expected media type (recorded in the resolved manifest)
    public double? Scale { get; init; }                   // multiply numeric values before binding (e.g. seconds -> ms = 1000)
}
