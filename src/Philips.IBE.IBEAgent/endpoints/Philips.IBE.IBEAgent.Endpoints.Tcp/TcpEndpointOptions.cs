using Philips.IBE.IBEAgent.Security;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Endpoints.Tcp;

public sealed class TcpInboundOptions
{
    public CommunicationMode Mode { get; init; } = CommunicationMode.Inbound;
    public required int SourceEndpointId { get; init; }
    public required int Port { get; init; }
    public string BindAddress { get; init; } = "0.0.0.0";    // interface to listen on; 0.0.0.0 = all, 127.0.0.1 = loopback only
    public string Format { get; init; } = "hl7v2";
    public int MaxConcurrentMessages { get; init; } = 100;   // admission control (bounded, P4)
    public TlsOptions Tls { get; init; } = new();            // Plain (default) | OneWay | Mutual (mTLS)
}

public sealed class TcpOutboundOptions
{
    public CommunicationMode Mode { get; init; } = CommunicationMode.Outbound;
    public int? SourceEndpointId { get; init; }
    public int? DuplexInboundSourceEndpointId { get; init; }
    public string InboundFormat { get; init; } = "hl7v2";
    public TimeSpan ReplyCorrelationTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(2);
    public required string Host { get; init; }
    public required int Port { get; init; }
    public int PoolSize { get; init; } = 8;                  // kill connection-per-message (P10)
    public bool ExpectReply { get; init; } = true;           // read MLLP ack frame (feeds enhanced ack / request-reply)
    public int ConnectRetryCount { get; init; } = 2;         // additional connection attempts after first dial
    public TimeSpan ConnectRetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TlsOptions Tls { get; init; } = new();            // Plain (default) | OneWay | Mutual (mTLS)
    public ProxyOptions Proxy { get; init; } = new();        // forward proxy (HTTP CONNECT tunnel), with/without credentials
}
