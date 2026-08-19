// HttpOutboundEndpoint.cs
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.Security;
namespace Philips.IBE.IBEAgent.Endpoints.Http;

public sealed class HttpOutboundEndpoint : IOutboundEndpoint, IDisposable
{
    private readonly HttpOutboundOptions _options;
    private readonly IMessageCodec? _codec;
    private readonly ILogger<HttpOutboundEndpoint> _logger;
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly IHttpSendRetryPolicy _retryPolicy;

    // Prefer passing a pooled HttpClient (IHttpClientFactory) from the host in Phase 7.
    public HttpOutboundEndpoint(
        HttpOutboundOptions options,
        IMessageCodec? codec,
        HttpClient? http = null,
        ILogger<HttpOutboundEndpoint>? logger = null)
    {
        _options = options;
        _codec = codec;
        _logger = logger ?? NullLogger<HttpOutboundEndpoint>.Instance;
        _retryPolicy = new HttpSendRetryPolicy();

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

            if (_options.Ssl.IsEnabled)
            {
                handler.SslOptions.EnabledSslProtocols = _options.Ssl.Protocols;
                handler.SslOptions.RemoteCertificateValidationCallback = _options.Ssl.CreateRemoteCertificateValidator();
                handler.SslOptions.CertificateRevocationCheckMode = _options.Ssl.CheckCertificateRevocation
                    ? System.Security.Cryptography.X509Certificates.X509RevocationMode.Online
                    : System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck;

                if (_options.Ssl.HasLocalCertificate())
                {
                    var clientCertificate = _options.Ssl.LoadLocalCertificate()
                        ?? throw new InvalidOperationException(
                            $"HTTP outbound endpoint ({_options.Endpoint}) has a client certificate reference that could not be resolved.");
                    handler.SslOptions.ClientCertificates = [clientCertificate];
                }
            }

            if (_options.Proxy.IsEnabled)
            {
                var proxyUri = new Uri($"http://{_options.Proxy.Host}:{_options.Proxy.Port}");
                var webProxy = new WebProxy(proxyUri);
                if (_options.Proxy.HasCredentials)
                    webProxy.Credentials = new NetworkCredential(_options.Proxy.Username, _options.Proxy.Password);

                handler.Proxy = webProxy;
                handler.UseProxy = true;
            }

            _http = new HttpClient(handler) { Timeout = _options.Timeout };
            _ownsClient = true;                  // disposing HttpClient disposes the handler too
        }
    }

    public async Task<DeliveryResult> SendAsync(MessageContext context, CancellationToken cancellationToken)
    {
        try
        {
            var wire = _codec?.Encode(context) ?? context.Payload;
            using var response = await _retryPolicy.SendAsync(
                sendAsync: ct => SendHttpRequestAsync(context, wire, ct),
                options: _options,
                logger: _logger,
                cancellationToken: cancellationToken);
            using var content = new ByteArrayContent(wire.ToArray());
            // An upstream-decided media type (relay or the media-type stage) wins; otherwise the endpoint default.
            var mediaType = context.Headers.TryGetValue(ContentHeaders.ContentType, out var contentType) && !string.IsNullOrWhiteSpace(contentType)
                ? contentType
                : _options.ContentType;
            content.Headers.TryAddWithoutValidation("Content-Type", mediaType);

            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            return response.IsSuccessStatusCode
                ? new DeliveryResult(DeliveryOutcome.Delivered, ResponsePayload: body, ResponseFormat: context.Format)
                : new DeliveryResult(DeliveryOutcome.Failed, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _logger.LogWarning(ex,
                "HTTP outbound send to {Endpoint} failed.",
                _options.Endpoint);
            return new DeliveryResult(DeliveryOutcome.Failed, ex.Message);
        }
    }

    private async Task<HttpResponseMessage> SendHttpRequestAsync(MessageContext context, ReadOnlyMemory<byte> wire, CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(wire.ToArray());
        content.Headers.TryAddWithoutValidation("Content-Type", _options.ContentType);

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint) { Content = content };
        request.Headers.TryAddWithoutValidation(TransportCorrelationHeaders.WireRequestId, context.CorrelationId);
        request.Headers.TryAddWithoutValidation(TransportCorrelationHeaders.WireMessageId, context.MessageId.ToString("N"));
        if (!string.IsNullOrWhiteSpace(_options.LogicalEndpointId))
            request.Headers.TryAddWithoutValidation(TransportCorrelationHeaders.WireLogicalEndpointId, _options.LogicalEndpointId);

        foreach (var (key, value) in context.Headers)   // opt-in metadata (fwd.*) -> protocol headers
            if (ForwardHeaders.TryGetName(key, out var name))
                request.Headers.TryAddWithoutValidation(name, value);

        return await _http.SendAsync(request, cancellationToken);
    }

    public void Dispose() { if (_ownsClient) _http.Dispose(); }
}
