namespace Philips.IBE.IBEAgent.Configuration;

// §3.6 (ADR 0001) — a named developer-shipped resource a stage consumes (e.g. a filter ruleset file).
// Ref is the default file's path relative to the configured resources root; ContentType is its media
// type (recorded in the resolved manifest). An FSE may point a Kind:file Setting at this name, or at
// their own file — either way the resolved path is confined to the allowed resources root.
public sealed record ResourceDefinition
{
    public string? ContentType { get; init; }
    public string? Ref { get; init; }
}
