namespace Philips.IBE.IBEAgent.Abstractions;

public interface IBatchCodec               // N messages -> 1 artifact (avro-zip, ndjson, ...).
{
    Task<ReadOnlyMemory<byte>> EncodeAsync(IReadOnlyList<MessageContext> batch, CancellationToken cancellationToken);
}