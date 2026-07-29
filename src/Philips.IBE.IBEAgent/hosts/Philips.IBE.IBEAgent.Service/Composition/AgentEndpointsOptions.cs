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
}

public sealed class HttpOutboundEndpointConfig
{
    public required int OutputId { get; init; }
    public required Uri Endpoint { get; init; }
    public string ContentType { get; init; } = "application/octet-stream";
    public int TimeoutSeconds { get; init; } = 30;
}

// Host-level config wrapper — binds the "Endpoints" section (contractData.json) into what the
// composition root needs to construct real Tcp/Http in/out endpoints and feed them into the ComponentRegistry.
public sealed class AgentEndpointsOptions
{
    public IReadOnlyList<Endpoints.Tcp.TcpInboundOptions> TcpInbound { get; init; } = [];
    public IReadOnlyList<TcpOutboundEndpointConfig> TcpOutbound { get; init; } = [];
    public IReadOnlyList<Endpoints.Http.HttpInboundOptions> HttpInbound { get; init; } = [];
    public IReadOnlyList<HttpOutboundEndpointConfig> HttpOutbound { get; init; } = [];
}
