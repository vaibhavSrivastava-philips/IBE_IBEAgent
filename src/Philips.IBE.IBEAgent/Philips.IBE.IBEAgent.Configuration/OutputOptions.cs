using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Configuration;

// §8 — one entry per output leg. No per-output pipeline (YAGNI): all processing already
// happened once in the contract's shared pipeline; formatting is the codec (Encoding/Batching.Codec).
public sealed record OutputOptions
{
    public required int OutputId { get; init; }
    public bool Required { get; init; } = true;
    public IReadOnlyList<int>? FromInputIds { get; init; }   // null/empty = all inputs (default)
    public DeliveryGuarantee DeliveryGuarantee { get; init; } = DeliveryGuarantee.AtMostOnce;
    public ChannelOptions Channel { get; init; } = new();
    public string Encoding { get; init; } = "hl7v2";         // names a catalog Codecs entry (IMessageCodec)
    public BatchingOptions? Batching { get; init; }
    public RetryOptions Retry { get; init; } = new();
}
