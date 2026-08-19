namespace Philips.IBE.IBEAgent.Configuration;

// §8 — developer-owned catalog: named, reusable building blocks that FSEs reference by name only
// (they never assemble stages or wire up codecs). Root of catalogData.json.
public sealed record CatalogOptions
{
    // Named SHARED pipelines: an ordered list of stage names. Each name is resolved to an
    // IMessageStage through the ComponentRegistry when the pipeline is compiled (PipelineBuilder, Core).
    // (Concurrent `parallel` composites are a deferred future feature — see §3.6a.)
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Pipelines { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    // Named codec bindings (message + batch), referenced by Output.Encoding / Output.Batching.Codec.
    public IReadOnlyDictionary<string, CodecOptions> Codecs { get; init; } =
        new Dictionary<string, CodecOptions>(StringComparer.Ordinal);

    // Named per-leg encoding bundles (message codec + optional batch codec), referenced by
    // Workflow.Format or Output.Format. The developer's "how a leg renders bytes" building block.
    public IReadOnlyDictionary<string, OutputFormatOptions> Formats { get; init; } =
        new Dictionary<string, OutputFormatOptions>(StringComparer.Ordinal);

    // Named contract blueprints (shared Pipeline + default Format) that FSE contracts reference by
    // name. Bundles only developer/code concerns; message-level/operational settings stay FSE-owned.
    public IReadOnlyDictionary<string, ContractWorkflowOptions> Workflows { get; init; } =
        new Dictionary<string, ContractWorkflowOptions>(StringComparer.Ordinal);

    // Optional extension -> media type map consumed by the "media-type" classifier stage (e.g. ".pdf" ->
    // "application/pdf"). Empty/absent = the stage is a no-op and a header-capable output keeps its own
    // configured ContentType. Developer-owned data, so classification stays plug-and-play (no hardcoding).
    public IReadOnlyDictionary<string, string> MediaTypes { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // Named developer-shipped resources (default files) a stage may consume via a Kind:file Setting. The
    // FSE points a file Setting at one of these names, or at their own file — either way the resolved path
    // is confined to the allowed resources root (see ResourceResolver).
    public IReadOnlyDictionary<string, ResourceDefinition> Resources { get; init; } =
        new Dictionary<string, ResourceDefinition>(StringComparer.Ordinal);
}
