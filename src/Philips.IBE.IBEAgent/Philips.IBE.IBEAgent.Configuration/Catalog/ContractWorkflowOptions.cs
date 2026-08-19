namespace Philips.IBE.IBEAgent.Configuration;

// §8 — developer-owned contract blueprint: the single thing an FSE references from a contract.
// Bundles the shared processing pipeline (runs once before fan-out) with the default per-leg output
// Format. Holds ONLY "plug-and-play code" concerns — no message-level/operational settings
// (Acknowledgement, Retry, DeliveryGuarantee, Channel, batch triggers) live here; those stay on the
// FSE contract. Root entries live under Catalog.Workflows and are keyed by name.
public sealed record ContractWorkflowOptions
{
    public int? Version { get; init; }       // governance: bump when a locked value or a default changes (a fleet-wide lever)
    public string? Pipeline { get; init; }   // names a catalog Pipelines entry (shared stages); null = no processing stages
    public string? Format { get; init; }     // single default per-leg encoding bundle (shorthand). Ignored when Formats is set.
    public IReadOnlyList<string>? Formats { get; init; } // ordered declared set (each names a catalog Formats entry); [0] = default. When >1, each output picks one via Output.Format (must be a member); an output that omits it falls back to [0] (logged).
    public IReadOnlyDictionary<string, SettingDefinition>? Settings { get; init; } // the flat FSE-facing form: friendly name -> guardrails + hidden Bind. ReplyOnFilter (and any other delegated knob) is expressed here.
}
