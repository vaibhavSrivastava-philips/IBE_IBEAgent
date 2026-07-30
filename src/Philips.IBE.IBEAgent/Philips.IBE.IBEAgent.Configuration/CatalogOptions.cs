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
    // Template.Format or Output.Format. The developer's "how a leg renders bytes" building block.
    public IReadOnlyDictionary<string, OutputFormatOptions> Formats { get; init; } =
        new Dictionary<string, OutputFormatOptions>(StringComparer.Ordinal);

    // Named contract blueprints (shared Pipeline + default Format) that FSE contracts reference by
    // name. Bundles only developer/code concerns; message-level/operational settings stay FSE-owned.
    public IReadOnlyDictionary<string, ContractTemplateOptions> Templates { get; init; } =
        new Dictionary<string, ContractTemplateOptions>(StringComparer.Ordinal);
}
