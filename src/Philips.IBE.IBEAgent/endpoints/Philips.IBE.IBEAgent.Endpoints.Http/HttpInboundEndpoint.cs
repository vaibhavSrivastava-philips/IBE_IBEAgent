using System.Net;
using System.Net.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.Security;
namespace Philips.IBE.IBEAgent.Endpoints.Http;

public sealed class HttpInboundEndpoint : IInboundEndpoint, IAsyncDisposable
{
    private readonly HttpInboundOptions _options;
    private readonly IMessageDispatcher _dispatcher;
    private readonly IReplyContextFactory _replyFactory;
    private readonly SemaphoreSlim _admission;
    private readonly ILogger<HttpInboundEndpoint> _logger;
    private readonly HttpListener _listener = new();
    private readonly RemoteCertificateValidationCallback? _clientCertValidator;
    private readonly string _listenerPrefix;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public HttpInboundEndpoint(HttpInboundOptions options, IMessageDispatcher dispatcher, IReplyContextFactory replyFactory, ILogger<HttpInboundEndpoint>? logger = null)
    {
        _options = options; _dispatcher = dispatcher; _replyFactory = replyFactory;
        _admission = new SemaphoreSlim(options.MaxConcurrentRequests);

        if (options.Ssl.IsEnabled && !options.Prefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"HTTP inbound endpoint has SSL mode {options.Ssl.Mode} configured but Prefix '{options.Prefix}' is not https://. " +
                "The server certificate itself must be bound to the port out-of-process (e.g. via netsh http add sslcert).");

        if (options.Ssl.RequiresClientCertificate())
            _clientCertValidator = options.Ssl.CreateRemoteCertificateValidator();

        _listenerPrefix = NormalizeHttpListenerPrefix(options.Prefix);
        _logger = logger ?? NullLogger<HttpInboundEndpoint>.Instance;
        _listener.Prefixes.Add(_listenerPrefix);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_cts.Token);
        _logger.LogInformation(
            "HTTP inbound endpoint (source {SourceEndpointId}) listening on {Prefix}.",
            _options.SourceEndpointId, _listenerPrefix);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_listener.IsListening) _listener.Stop();
        if (_acceptLoop is not null)
            try { await _acceptLoop.WaitAsync(cancellationToken); } catch (OperationCanceledException) { }
        _logger.LogInformation("HTTP inbound endpoint (source {SourceEndpointId}) stopped.", _options.SourceEndpointId);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext http;
            try { http = await _listener.GetContextAsync().WaitAsync(ct); }
            catch (OperationCanceledException) { break; }                  // expected on shutdown
            catch (HttpListenerException ex)
            {
                _logger.LogDebug(ex, "HTTP inbound accept loop (source {SourceEndpointId}) ended.", _options.SourceEndpointId);
                break;
            }
            _ = HandleRequestAsync(http, ct);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext http, CancellationToken ct)
    {
        await _admission.WaitAsync(ct);
        var token = new HttpResponseAckToken(http.Response, _logger);
        try
        {
            if (_options.Ssl.RequiresClientCertificate() && !await ValidateClientCertificateAsync(http, ct))
            {
                token.CompleteWithError(403);
                return;
            }

            using var ms = new MemoryStream();
            await http.Request.InputStream.CopyToAsync(ms, ct);

            var reply = _replyFactory.Create(_options.SourceEndpointId, token);
            var headers = BuildHeaders(http.Request);
            var correlationId = headers.TryGetValue(TransportCorrelationHeaders.RequestId, out var requestId) && !string.IsNullOrWhiteSpace(requestId)
                ? requestId
                : Guid.NewGuid().ToString("N");
            var ctx = new MessageContext(
                correlationId: correlationId,
                sourceEndpointId: _options.SourceEndpointId,
                format: _options.Format,
                ack: token,
                reply: reply,
                payload: ms.ToArray(),
                headers: headers);

            // Monitoring (Information) — per-message receipt for the production flow.
            _logger.LogInformation(
                "Received request {CorrelationId} ({ByteCount} bytes) on HTTP source {SourceEndpointId}.",
                ctx.CorrelationId, ms.Length, _options.SourceEndpointId);

            // Deepest level (Trace) — the full request body. Guarded so the decode only runs at Trace.
            if (_logger.IsEnabled(LogLevel.Trace))
                _logger.LogTrace(
                    "Inbound request {CorrelationId} body: {Message}",
                    ctx.CorrelationId, MessagePreview.ForLog(ctx.Payload.Span));

            ctx.Reply.Attach(ctx);
            await _dispatcher.DispatchAsync(ctx, ct);
            await token.Completion.WaitAsync(_options.ReplyTimeout, ct); // hold request until reply/timeout (§6.1)
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Reply timed out after {TimeoutMs}ms on HTTP source {SourceEndpointId}; responding 504.",
                _options.ReplyTimeout.TotalMilliseconds, _options.SourceEndpointId);
            token.CompleteWithError(504);
        }
        catch (OperationCanceledException) { token.CompleteWithError(503); } // shutdown; expected
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unhandled error processing HTTP request on source {SourceEndpointId}; responding 500.",
                _options.SourceEndpointId);
            token.CompleteWithError(500);
        }
        finally { _admission.Release(); }
    }

    // TwoWay mode: HttpListener performs the TLS handshake itself (certificate bound to the port at
    // the OS level), but client-certificate *validation* still runs here via the same
    // RemoteCertificateValidationCallback shape used by TCP, for a consistent SSL policy.
    private async Task<bool> ValidateClientCertificateAsync(HttpListenerContext http, CancellationToken ct)
    {
        var clientCert = await http.Request.GetClientCertificateAsync().WaitAsync(ct);
        if (clientCert is null) return false;

        using var chain = new System.Security.Cryptography.X509Certificates.X509Chain();
        var built = chain.Build(clientCert);
        var errors = built ? SslPolicyErrors.None : SslPolicyErrors.RemoteCertificateChainErrors;
        return _clientCertValidator!(this, clientCert, chain, errors);
    }

    private Dictionary<string, string> BuildHeaders(HttpListenerRequest request)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);

        AddIfPresent(TransportCorrelationHeaders.WireRequestId, TransportCorrelationHeaders.RequestId);
        AddIfPresent(TransportCorrelationHeaders.WireMessageId, TransportCorrelationHeaders.MessageId);
        AddIfPresent(TransportCorrelationHeaders.WireLogicalEndpointId, TransportCorrelationHeaders.LogicalEndpointId);

        // Transparent content-type passthrough: surface the request's media type for a downstream output to honor.
        if (_options.RelayContentType && !string.IsNullOrWhiteSpace(request.ContentType))
            headers[ContentHeaders.ContentType] = request.ContentType;

        return headers;

        void AddIfPresent(string wireName, string headerName)
        {
            var value = request.Headers[wireName];
            if (!string.IsNullOrWhiteSpace(value))
                headers[headerName] = value;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _cts?.Dispose();
        _admission.Dispose();
        ((IDisposable)_listener).Dispose();
    }

    private static string NormalizeHttpListenerPrefix(string prefix)
        => prefix.EndsWith("/", StringComparison.Ordinal) ? prefix : prefix + "/";
}
