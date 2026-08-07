// HttpOutboundEndpoint.cs
using Philips.IBE.IBEAgent.Abstractions;
namespace Philips.IBE.IBEAgent.Endpoints.Http;

public sealed class HttpOutboundEndpoint : IOutboundEndpoint, IDisposable
{
    private readonly HttpOutboundOptions _options;
    private readonly IMessageCodec? _codec;
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    // Prefer passing a pooled HttpClient (IHttpClientFactory) from the host in Phase 7.
    public HttpOutboundEndpoint(HttpOutboundOptions options, IMessageCodec? codec, HttpClient? http = null)
    {
        _options = options;
        _codec = codec;

        if (http is not null)
        {
            _http = http;                        // host-provided (IHttpClientFactory) — preferred in Phase 7
            _ownsClient = false;
        }
        else
        {
            // The handler IS the connection pool. One shared instance, reused for every send.
            var handler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer      = _options.MaxConnectionsPerServer,
                PooledConnectionLifetime     = _options.PooledConnectionLifetime,
                PooledConnectionIdleTimeout  = _options.PooledConnectionIdleTimeout,
            };
            _http = new HttpClient(handler) { Timeout = _options.Timeout };
            _ownsClient = true;                  // disposing HttpClient disposes the handler too
        }
    }

    public async Task<DeliveryResult> SendAsync(MessageContext context, CancellationToken cancellationToken)
    {
        try
        {
            var wire = _codec?.Encode(context) ?? context.Payload;
            using var content = new ByteArrayContent(wire.ToArray());
            content.Headers.TryAddWithoutValidation("Content-Type", _options.ContentType);

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint) { Content = content };
            foreach (var (key, value) in context.Headers)   // opt-in metadata (fwd.*) -> protocol headers
                if (ForwardHeaders.TryGetName(key, out var name))
                    request.Headers.TryAddWithoutValidation(name, value);

            using var response = await _http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            return response.IsSuccessStatusCode
                ? new DeliveryResult(DeliveryOutcome.Delivered, ResponsePayload: body, ResponseFormat: context.Format)
                : new DeliveryResult(DeliveryOutcome.Failed, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new DeliveryResult(DeliveryOutcome.Failed, ex.Message);
        }
    }

    public void Dispose() { if (_ownsClient) _http.Dispose(); }
}