namespace Philips.IBE.IBEAgent.Endpoints.Http;

public sealed class HttpInboundOptions
{
    public required int SourceEndpointId { get; init; }
    public required string Prefix { get; init; }             // e.g. "http://localhost:8080/ibe/"
    public string Format { get; init; } = "hl7v2";
    public int MaxConcurrentRequests { get; init; } = 200;
    public TimeSpan ReplyTimeout { get; init; } = TimeSpan.FromSeconds(30); // §6.1 held-connection bound
}

public sealed class HttpOutboundOptions
{
    public required Uri Endpoint { get; init; }
    public string ContentType { get; init; } = "application/octet-stream";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    // The HTTP "connection pool" knobs (map to SocketsHttpHandler):
    public int MaxConnectionsPerServer { get; init; } = 8;                        // ~ TCP PoolSize
    public TimeSpan PooledConnectionLifetime { get; init; } = TimeSpan.FromMinutes(5); // recycle to pick up DNS changes
    public TimeSpan PooledConnectionIdleTimeout { get; init; } = TimeSpan.FromMinutes(2);
}