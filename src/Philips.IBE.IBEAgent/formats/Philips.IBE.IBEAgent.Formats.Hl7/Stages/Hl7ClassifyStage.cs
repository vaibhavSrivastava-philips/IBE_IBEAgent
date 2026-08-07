using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Formats.Hl7;

// §3.6 — shared-pipeline stage that reads identifying fields from an HL7 v2 message (MSH-9 message
// type, MSH-10 control id) and logs them at Information so a flow is identifiable by the sender's own
// ids without dumping the body. The values are also stashed in Headers so downstream components/log
// sites can reuse them. Opt-in per contract: add "hl7-classify" to a pipeline for contracts that want
// id/type visibility; high-throughput contracts omit it and pay no parse cost. Fail-safe — non-HL7 or
// unparseable input passes through unchanged (classification never drops a message).
public sealed class Hl7ClassifyStage : IMessageStage
{
    public const string Name = "hl7-classify";

    // Well-known header keys downstream components / log sites can read.
    public const string MessageTypeHeader = "hl7.messageType";   // MSH-9  (e.g. ADT^A01)
    public const string MessageIdHeader = "hl7.messageId";       // MSH-10 (message control id)

    private readonly ILogger<Hl7ClassifyStage> _logger;

    public Hl7ClassifyStage(ILogger<Hl7ClassifyStage>? logger = null)
        => _logger = logger ?? NullLogger<Hl7ClassifyStage>.Instance;

    public Task<StageResult> ProcessAsync(MessageContext context)
    {
        if (TryReadMsh(context.Payload.Span, out var messageType, out var messageId))
        {
            if (messageType is not null) context.Headers[MessageTypeHeader] = messageType;
            if (messageId is not null) context.Headers[MessageIdHeader] = messageId;

            _logger.LogInformation(
                "HL7 message classified: type {MessageType}, control id {MessageId} (correlation {CorrelationId}) on source {SourceEndpointId}.",
                messageType ?? "(unknown)", messageId ?? "(none)", context.CorrelationId, context.SourceEndpointId);
        }

        return Task.FromResult(StageResult.Continue);
    }

    // Lightweight, fail-safe MSH read: decode only the first (MSH) segment and split on the field
    // separator. MSH-1 is the separator itself, so after splitting "MSH|^~\&|..." field MSH-N lands at
    // index N-1 -> MSH-9 (type) = index 8, MSH-10 (control id) = index 9. Never throws.
    private static bool TryReadMsh(ReadOnlySpan<byte> payload, out string? messageType, out string? messageId)
    {
        messageType = null;
        messageId = null;
        if (payload.IsEmpty)
            return false;

        var end = payload.IndexOfAny((byte)'\r', (byte)'\n');
        var msh = end >= 0 ? payload[..end] : payload;
        if (msh.Length < 3 || msh[0] != (byte)'M' || msh[1] != (byte)'S' || msh[2] != (byte)'H')
            return false;

        var fields = Encoding.UTF8.GetString(msh).Split('|');
        if (fields.Length > 8 && !string.IsNullOrWhiteSpace(fields[8])) messageType = fields[8];
        if (fields.Length > 9 && !string.IsNullOrWhiteSpace(fields[9])) messageId = fields[9];
        return messageType is not null || messageId is not null;
    }
}
