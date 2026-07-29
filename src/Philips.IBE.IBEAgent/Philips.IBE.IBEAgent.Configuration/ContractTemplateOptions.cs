namespace Philips.IBE.IBEAgent.Configuration;

// §8 — developer-owned contract blueprint: the single thing an FSE references from a contract.
// Bundles the shared processing pipeline (runs once before fan-out) with the default per-leg output
// Format. Holds ONLY "plug-and-play code" concerns — no message-level/operational settings
// (Acknowledgement, Retry, DeliveryGuarantee, Channel, batch triggers) live here; those stay on the
// FSE contract. Root entries live under Catalog.Templates and are keyed by name.
public sealed record ContractTemplateOptions
{
    public string? Pipeline { get; init; }   // names a catalog Pipelines entry (shared stages); null = no processing stages
    public string? Format { get; init; }     // names a catalog Formats entry (default per-leg encoding bundle)
}
