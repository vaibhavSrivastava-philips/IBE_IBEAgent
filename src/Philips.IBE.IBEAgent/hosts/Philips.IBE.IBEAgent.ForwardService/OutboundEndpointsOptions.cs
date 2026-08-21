using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Security;

namespace Philips.IBE.IBEAgent.ForwardService;

// Host-level config wrapper — mirrors Philips.IBE.IBEAgent.Service's AgentEndpointsOptions outbound
// shape (OutputId + transport config) so this out-of-process host can build the SAME kind of
// outbound endpoints the in-process engine uses, without depending on the Service host project.
public sealed class TcpOutboundEndpointConfig
{
    public CommunicationMode Mode { get; init; } = CommunicationMode.Outbound;
    public required int OutputId { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public int PoolSize { get; init; } = 8;
    public bool ExpectReply { get; init; } = true;
    public int ConnectRetryCount { get; init; } = 2;
    public int ConnectRetryDelayMilliseconds { get; init; } = 1000;
    public TlsOptions Tls { get; init; } = new();
    public ProxyOptions Proxy { get; init; } = new();
    public string Encoding { get; init; } = "hl7v2";
}

public sealed class HttpOutboundEndpointConfig
{
    public CommunicationMode Mode { get; init; } = CommunicationMode.Outbound;
    public required int OutputId { get; init; }
    public required Uri Endpoint { get; init; }
    public string ContentType { get; init; } = "application/octet-stream";
    public int TimeoutSeconds { get; init; } = 30;
    public int MaxConnectionsPerServer { get; init; } = 8;
    public int PooledConnectionLifetimeSeconds { get; init; } = 300;
    public int PooledConnectionIdleTimeoutSeconds { get; init; } = 120;
    public int ConnectRetryCount { get; init; } = 2;
    public int ConnectRetryDelayMilliseconds { get; init; } = 1000;
    public TlsOptions Tls { get; init; } = new();
    public ProxyOptions Proxy { get; init; } = new();
    public string Encoding { get; init; } = "hl7v2";
}

public sealed class WebSocketOutboundEndpointConfig
{
    public CommunicationMode Mode { get; init; } = CommunicationMode.Outbound;
    public required int OutputId { get; init; }
    public required Uri Endpoint { get; init; }
    public bool ExpectReply { get; init; } = true;
    public int ConnectRetryCount { get; init; } = 2;
    public int ConnectRetryDelayMilliseconds { get; init; } = 1000;
    public int PoolSize { get; init; } = 8;
    public int ReceiveBufferSize { get; init; } = 8192;
    public TlsOptions Tls { get; init; } = new();
    public ProxyOptions Proxy { get; init; } = new();
    public string Encoding { get; init; } = "hl7v2";
}

public sealed class FileOutboundEndpointConfig
{
    public CommunicationMode Mode { get; init; } = CommunicationMode.Outbound;
    public required int OutputId { get; init; }
    public required string Directory { get; init; }
    public string? FileNameTemplate { get; init; }
    public string DefaultExtension { get; init; } = "txt";
    public bool AllowMessageDirectedPath { get; init; } = true;   // honor an envelope's destinationpath as the output dir (legacy parity)
    public string? Encoding { get; init; }
}

// §3.9 — binds "Ibe:Endpoints:TcpOutbound"/"HttpOutbound"/"FileOutbound" so the out-of-process forward
// host can re-create the SAME outbound endpoints (by OutputId) the in-process engine uses for replay.
public sealed class OutboundEndpointsOptions
{
    public IReadOnlyList<TcpOutboundEndpointConfig> TcpOutbound { get; init; } = [];
    public IReadOnlyList<HttpOutboundEndpointConfig> HttpOutbound { get; init; } = [];
    public IReadOnlyList<WebSocketOutboundEndpointConfig> WebSocketOutbound { get; init; } = [];
    public IReadOnlyList<FileOutboundEndpointConfig> FileOutbound { get; init; } = [];
}
