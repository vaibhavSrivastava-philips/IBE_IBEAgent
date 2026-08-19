using Philips.IBE.IBEAgent.Security;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Endpoints.Http;

public sealed class HttpInboundOptions
{
    public CommunicationMode Mode { get; init; } = CommunicationMode.Inbound;
    public string? LogicalEndpointId { get; init; }
    public required int SourceEndpointId { get; init; }
    public required string Prefix { get; init; }             // e.g. "http://localhost:8080/ibe/" or "https://..." when Ssl is enabled
    public string Format { get; init; } = "hl7v2";
    public int MaxConcurrentRequests { get; init; } = 200;
    public TimeSpan ReplyTimeout { get; init; } = TimeSpan.FromSeconds(30); // §6.1 held-connection bound

    // HttpListener itself does not perform the TLS handshake (Windows binds the certificate to the
    // port via netsh/httpcfg); Ssl.Mode still governs *client certificate* enforcement (Mutual = mTLS)
    // and drives the composition root to require an https:// Prefix.
    public SslOptions Ssl { get; init; } = new();
}

public sealed class HttpOutboundOptions
{
    public CommunicationMode Mode { get; init; } = CommunicationMode.Outbound;
    public string? LogicalEndpointId { get; init; }
    public required Uri Endpoint { get; init; }
    public string ContentType { get; init; } = "application/octet-stream";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    // The HTTP "connection pool" knobs (map to SocketsHttpHandler):
    public int MaxConnectionsPerServer { get; init; } = 8;                        // ~ TCP PoolSize
    public TimeSpan PooledConnectionLifetime { get; init; } = TimeSpan.FromMinutes(5); // recycle to pick up DNS changes
    public TimeSpan PooledConnectionIdleTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public int ConnectRetryCount { get; init; } = 2;                              // additional attempts after first call
    public TimeSpan ConnectRetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public SslOptions Ssl { get; init; } = new();            // Plain (default) | OneWay | Mutual (mTLS via client cert)
    public ProxyOptions Proxy { get; init; } = new();        // forward proxy, with/without credentials
}
