namespace Philips.IBE.IBEAgent.Configuration;

// §8 — named codec binding: N -> 1 batch encoding (IBatchCodec), referenced by Output.Batching.Codec.
public sealed record BatchingOptions
{
    public bool Enabled { get; init; }
    public string? Codec { get; init; }                // null = inherit the batch codec from the Format/Template; set = inline override. names a catalog Codecs entry (IBatchCodec)
    public int MaxCount { get; init; } = 500;
    public int MaxLatencyMs { get; init; } = 10_000;
}
