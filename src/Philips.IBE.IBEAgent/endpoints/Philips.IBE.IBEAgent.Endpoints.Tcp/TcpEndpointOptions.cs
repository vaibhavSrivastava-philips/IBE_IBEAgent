using Philips.IBE.IBEAgent.Security;

namespace Philips.IBE.IBEAgent.Endpoints.Tcp;

public sealed class TcpInboundOptions
{
    public required int SourceEndpointId { get; init; }
    public required int Port { get; init; }
    public string Format { get; init; } = "hl7v2";
    public int MaxConcurrentMessages { get; init; } = 100;   // admission control (bounded, P4)
    public SslOptions Ssl { get; init; } = new();            // None (default) | OneWay | TwoWay (mutual TLS)
}

public sealed class TcpOutboundOptions
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public int PoolSize { get; init; } = 8;                  // kill connection-per-message (P10)
    public bool ExpectReply { get; init; } = true;           // read MLLP ack frame (feeds enhanced ack / request-reply)
    public SslOptions Ssl { get; init; } = new();            // None (default) | OneWay | TwoWay (mutual TLS)
    public ProxyOptions Proxy { get; init; } = new();        // forward proxy (HTTP CONNECT tunnel), with/without credentials
}
