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

    // Connection-pool knobs (parity with TCP PoolSize) — optional; omit to keep the endpoint's own
    // SocketsHttpHandler defaults. Lifetime/idle are in seconds and mapped to TimeSpans in the builder.
    public int MaxConnectionsPerServer { get; init; } = 8;                       // ~ TCP PoolSize
    public int PooledConnectionLifetimeSeconds { get; init; } = 300;             // 5 min — recycle to pick up DNS changes
    public int PooledConnectionIdleTimeoutSeconds { get; init; } = 120;          // 2 min
}

public sealed class FileOutboundEndpointConfig
{
    public required int OutputId { get; init; }
    public required string Directory { get; init; }
    public string? FileNameTemplate { get; init; }        // null/blank -> FileNameResolver default (timestamp+correlationId)
    public string DefaultExtension { get; init; } = "txt";
}

// Host-level config wrapper — binds the "Endpoints" section (contractData.json) into what the
// composition root needs to construct real Tcp/Http/File in/out endpoints and feed them into the ComponentRegistry.
public sealed class AgentEndpointsOptions
{
    public IReadOnlyList<Endpoints.Tcp.TcpInboundOptions> TcpInbound { get; init; } = [];
    public IReadOnlyList<TcpOutboundEndpointConfig> TcpOutbound { get; init; } = [];
    public IReadOnlyList<Endpoints.Http.HttpInboundOptions> HttpInbound { get; init; } = [];
    public IReadOnlyList<HttpOutboundEndpointConfig> HttpOutbound { get; init; } = [];
    public IReadOnlyList<Endpoints.File.FileInboundOptions> FileInbound { get; init; } = [];
    public IReadOnlyList<FileOutboundEndpointConfig> FileOutbound { get; init; } = [];
}
