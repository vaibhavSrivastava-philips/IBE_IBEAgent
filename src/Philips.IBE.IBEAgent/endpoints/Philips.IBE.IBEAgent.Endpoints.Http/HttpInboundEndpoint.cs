using System.Net;
using System.Net.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Philips.IBE.IBEAgent.Abstractions;
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

        if (options.Ssl.RequiresRemoteCertificate)
            _clientCertValidator = options.Ssl.CreateRemoteCertificateValidator();

        _logger = logger ?? NullLogger<HttpInboundEndpoint>.Instance;
        _listener.Prefixes.Add(options.Prefix);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_cts.Token);
        _logger.LogInformation(
            "HTTP inbound endpoint (source {SourceEndpointId}) listening on {Prefix}.",
            _options.SourceEndpointId, _options.Prefix);
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
        var token = new HttpResponseAckToken(http.Response);
        try
        {
            if (_options.Ssl.RequiresRemoteCertificate && !await ValidateClientCertificateAsync(http, ct))
            {
                token.CompleteWithError(403);
                return;
            }

            using var ms = new MemoryStream();
            await http.Request.InputStream.CopyToAsync(ms, ct);

            var reply = _replyFactory.Create(_options.SourceEndpointId, token);
            var ctx = new MessageContext(
                correlationId: Guid.NewGuid().ToString("N"),
                sourceEndpointId: _options.SourceEndpointId,
                format: _options.Format,
                ack: token,
                reply: reply,
                payload: ms.ToArray());

            _logger.LogDebug(
                "Received request {CorrelationId} ({ByteCount} bytes) on HTTP source {SourceEndpointId}.",
                ctx.CorrelationId, ms.Length, _options.SourceEndpointId);

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

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _cts?.Dispose();
        _admission.Dispose();
        ((IDisposable)_listener).Dispose();
    }
}
