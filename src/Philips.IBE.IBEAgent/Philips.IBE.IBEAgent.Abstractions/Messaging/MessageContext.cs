namespace Philips.IBE.IBEAgent.Abstractions;

// P1 — one message, one envelope, no side-channels. INV-5 payload = canonical bytes; INV-6 Reply owned at reception.
public sealed class MessageContext
{
    public Guid MessageId { get; } = Guid.NewGuid();
    public string CorrelationId { get; }
    public int SourceEndpointId { get; }
    public string Format { get; }                      // per-input tag (INV-1): selects parser/stages/formatter
    public ReadOnlyMemory<byte> Payload { get; private set; }  // canonical source bytes (INV-5)
    public object? ParsedView { get; set; }            // lazily-parsed model, built once by the parse stage
    public IDictionary<string, string> Headers { get; } // mutable during shared pipeline; read-only after fan-out (A5)
    public IAckToken Ack { get; }
    public IReplyContext Reply { get; }                // shared by reference across all leg clones
    public int LegOutputId { get; private set; }
    public bool IsReplay { get; private set; }         // set on store-and-forward replay -> suppresses re-reply

    public MessageContext(
        string correlationId,
        int sourceEndpointId,
        string format,
        IAckToken ack,
        IReplyContext reply,
        ReadOnlyMemory<byte> payload = default,
        IDictionary<string, string>? headers = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(correlationId);
        ArgumentException.ThrowIfNullOrEmpty(format);
        ArgumentNullException.ThrowIfNull(ack);
        ArgumentNullException.ThrowIfNull(reply);

        CorrelationId = correlationId;
        SourceEndpointId = sourceEndpointId;
        Format = format;
        Ack = ack;
        Reply = reply;
        Payload = payload;
        Headers = headers ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    // Per-leg branch (§13): SHARES immutable payload, parsed view, and headers by reference (no copy, F-perf4).
    private MessageContext(MessageContext source, int legOutputId)
    {
        CorrelationId = source.CorrelationId;
        SourceEndpointId = source.SourceEndpointId;
        Format = source.Format;
        Ack = source.Ack;
        Reply = source.Reply;
        Payload = source.Payload;
        ParsedView = source.ParsedView;
        Headers = source.Headers;
        LegOutputId = legOutputId;
    }

    public void ReplacePayload(ReadOnlyMemory<byte> payload) => Payload = payload;
    public void MarkReplay() => IsReplay = true;
    public MessageContext CloneForLeg(int outputId) => new(this, outputId);
}