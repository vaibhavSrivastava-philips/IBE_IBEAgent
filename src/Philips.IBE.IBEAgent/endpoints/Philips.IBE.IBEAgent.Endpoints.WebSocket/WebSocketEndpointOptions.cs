using Philips.IBE.IBEAgent.Security;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Endpoints.WebSocket;

public sealed class WebSocketInboundOptions
{
    public CommunicationMode Mode { get; init; } = CommunicationMode.Inbound;
    public required int SourceEndpointId { get; init; }
    public required string Prefix { get; init; }             // e.g. "http://localhost:8080/ibe/ws/" or "https://..." when Ssl is enabled
    public string Format { get; init; } = "hl7v2";
    public int MaxConcurrentMessages { get; init; } = 100;   // admission control (bounded, P4), mirrors Tcp
    public int ReceiveBufferSize { get; init; } = 8192;

    // Same story as HttpInboundOptions.Ssl: HttpListener performs the TLS handshake itself (server
    // certificate bound to the port out-of-process); Ssl.Mode still governs client-certificate
    // enforcement (TwoWay = mTLS) and drives the composition root to require a wss/https Prefix.
    public SslOptions Ssl { get; init; } = new();
}

public sealed class WebSocketOutboundOptions
{
    public CommunicationMode Mode { get; init; } = CommunicationMode.Outbound;
    public int? SourceEndpointId { get; init; }
    public int? DuplexInboundSourceEndpointId { get; init; }
    public string InboundFormat { get; init; } = "hl7v2";
    public required Uri Endpoint { get; init; }              // ws:// or wss://
    public bool ExpectReply { get; init; } = true;            // read one reply message back (feeds enhanced ack / request-reply)
    public TimeSpan ReplyCorrelationTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(2);
    public int PoolSize { get; init; } = 8;                   // pooled persistent connections (parity with TCP PoolSize)
    public int ReceiveBufferSize { get; init; } = 8192;
    public SslOptions Ssl { get; init; } = new();             // None (default) | OneWay | TwoWay (mutual TLS via client cert)
    public ProxyOptions Proxy { get; init; } = new();         // forward proxy, with/without credentials
}
