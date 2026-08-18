namespace Philips.IBE.IBEAgent.Configuration;

// §8 — a named binding: a registered codec Type + its params. Message codecs (IMessageCodec,
// one message -> bytes) are referenced by Output.Encoding; batch codecs (IBatchCodec, N -> 1)
// by Output.Batching.Codec. Params are opaque here (pure DTO, no delegates, P8) — the
// ComponentRegistry (Core) interprets Type + Params when constructing the real codec.
public sealed record CodecOptions
{
    public required string Type { get; init; }
    public IReadOnlyDictionary<string, string> Params { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
