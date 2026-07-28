using System.Text.Json;
using System.Text.Json.Serialization;

namespace Philips.IBE.IBEAgent.Persistence;

// §3.9 — everything ReplayEnvelope needs to reconstruct a leg-targeted MessageContext without
// re-running the shared pipeline: the post-pipeline canonical payload + header snapshot for
// THIS leg (INV-5), plus the routing fields the leg's codec/endpoint still need.
internal sealed class ReplayEnvelope
{
    public Guid MessageId { get; init; }
    public required string CorrelationId { get; init; }
    public int SourceEndpointId { get; init; }
    public required string Format { get; init; }
    public required Dictionary<string, string> Headers { get; init; }
    public required byte[] Payload { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public byte[] ToPlaintext() => JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);

    public static ReplayEnvelope FromPlaintext(byte[] plaintext)
        => JsonSerializer.Deserialize<ReplayEnvelope>(plaintext, JsonOptions)
           ?? throw new InvalidOperationException("Corrupt forward-store envelope: deserialized to null.");
}
