using Philips.IBE.IBEAgent.Security;

namespace Philips.IBE.IBEAgent.Service;

// Host-level config wrapper — a Tcp outbound endpoint config carries an OutputId (leg identity)
// that TcpOutboundOptions itself doesn't need to know about (it's purely transport config).
public sealed class TcpOutboundEndpointConfig
{
    public required int OutputId { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public int PoolSize { get; init; } = 8;
    public bool ExpectReply { get; init; } = true;
    public SslOptions Ssl { get; init; } = new();            // None (default) | OneWay | TwoWay (mutual TLS)
    public ProxyOptions Proxy { get; init; } = new();        // forward proxy (HTTP CONNECT tunnel), with/without credentials
}

public sealed class HttpOutboundEndpointConfig
{
    public required int OutputId { get; init; }
    public required Uri Endpoint { get; init; }
    public string ContentType { get; init; } = "application/octet-stream";
    public int TimeoutSeconds { get; init; } = 30;

    // Connection-pool knobs (parity with TCP PoolSize) — optional; omit to keep the endpoint's own
    // SocketsHttpHandler defaults. Lifetime/idle are in seconds and mapped to TimeSpans in the builder.
    public int MaxConnectionsPerServer { get; init; } = 8;                       // ~ TCP PoolSize
    public int PooledConnectionLifetimeSeconds { get; init; } = 300;             // 5 min — recycle to pick up DNS changes
    public int PooledConnectionIdleTimeoutSeconds { get; init; } = 120;          // 2 min
    public SslOptions Ssl { get; init; } = new();            // None (default) | OneWay | TwoWay (mutual TLS via client cert)
    public ProxyOptions Proxy { get; init; } = new();        // forward proxy, with/without credentials
}

// Host-level config wrapper — a WebSocket outbound endpoint config carries an OutputId (leg
// identity) that WebSocketOutboundOptions itself doesn't need to know about.
public sealed class WebSocketOutboundEndpointConfig
{
    public required int OutputId { get; init; }
    public required Uri Endpoint { get; init; }              // ws:// or wss://
    public bool ExpectReply { get; init; } = true;
    public int PoolSize { get; init; } = 8;                   // ~ TCP PoolSize
    public int ReceiveBufferSize { get; init; } = 8192;
    public SslOptions Ssl { get; init; } = new();            // None (default) | OneWay | TwoWay (mutual TLS via client cert)
    public ProxyOptions Proxy { get; init; } = new();        // forward proxy, with/without credentials
}

// Host-level config wrapper — binds the "Endpoints" section (contractData.json) into what the
// composition root needs to construct real Tcp/Http in/out endpoints and feed them into the ComponentRegistry.
public sealed class AgentEndpointsOptions
{
    public IReadOnlyList<Endpoints.Tcp.TcpInboundOptions> TcpInbound { get; init; } = [];
    public IReadOnlyList<TcpOutboundEndpointConfig> TcpOutbound { get; init; } = [];
    public IReadOnlyList<Endpoints.Http.HttpInboundOptions> HttpInbound { get; init; } = [];
    public IReadOnlyList<HttpOutboundEndpointConfig> HttpOutbound { get; init; } = [];
    public IReadOnlyList<Endpoints.WebSocket.WebSocketInboundOptions> WebSocketInbound { get; init; } = [];
    public IReadOnlyList<WebSocketOutboundEndpointConfig> WebSocketOutbound { get; init; } = [];
}
