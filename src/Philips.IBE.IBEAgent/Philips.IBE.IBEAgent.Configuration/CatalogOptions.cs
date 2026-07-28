namespace Philips.IBE.IBEAgent.Configuration;

// §8 — developer-owned catalog: named, reusable building blocks that FSEs reference by name only
// (they never assemble stages or wire up codecs). Root of catalogData.json.
public sealed record CatalogOptions
{
    // Named SHARED pipelines: an ordered list of stage-name tokens. Each entry is either a plain
    // stage name (string) or a nested `parallel` composite (ParallelStageOptions) — modeled here as
    // `object` to keep the DTO pure/serializer-agnostic; the PipelineBuilder (Core) discriminates by
    // shape (string vs. an object with Type == "parallel") when compiling.
    public IReadOnlyDictionary<string, IReadOnlyList<object>> Pipelines { get; init; } =
        new Dictionary<string, IReadOnlyList<object>>(StringComparer.Ordinal);

    // Named codec bindings (message + batch), referenced by Output.Encoding / Output.Batching.Codec.
    public IReadOnlyDictionary<string, CodecOptions> Codecs { get; init; } =
        new Dictionary<string, CodecOptions>(StringComparer.Ordinal);
}
