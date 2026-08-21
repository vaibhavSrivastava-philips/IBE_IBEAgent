using System.Net;
using System.Net.Security;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Security;
namespace Philips.IBE.IBEAgent.Endpoints.WebSocket;

public sealed class WebSocketInboundEndpoint : IInboundEndpoint, IAsyncDisposable
{
    private readonly WebSocketInboundOptions _options;
    private readonly IMessageDispatcher _dispatcher;
    private readonly IReplyContextFactory _replyFactory;
    private readonly ILogger<WebSocketInboundEndpoint> _logger;
    private readonly SemaphoreSlim _admission;
    private readonly HttpListener _listener = new();
    private readonly RemoteCertificateValidationCallback? _clientCertValidator;
    private readonly WebSocketDuplexSessionRegistry? _duplexSessions;
    private readonly string _listenerPrefix;
    private readonly HttpTlsPortBinding? _tlsBinding;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public WebSocketInboundEndpoint(
        WebSocketInboundOptions options,
        IMessageDispatcher dispatcher,
        IReplyContextFactory replyFactory,
        WebSocketDuplexSessionRegistry? duplexSessions = null,
        ILogger<WebSocketInboundEndpoint>? logger = null)
    {
        _options = options;
        _dispatcher = dispatcher;
        _replyFactory = replyFactory;
        _logger = logger ?? NullLogger<WebSocketInboundEndpoint>.Instance;
        _admission = new SemaphoreSlim(options.MaxConcurrentMessages);
        _duplexSessions = duplexSessions;

        _tlsBinding = HttpTlsPortBinding.Create(options.Tls, options.Prefix, options.SourceEndpointId, "WebSocket");

        if (options.Tls.RequiresClientCertificate())
            _clientCertValidator = options.Tls.CreateRemoteCertificateValidator();

        _listenerPrefix = NormalizeHttpListenerPrefix(options.Prefix);
        _listener.Prefixes.Add(_listenerPrefix);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_tlsBinding is not null)
        {
            _tlsBinding.Bind();
            _logger.LogInformation(
                "WS inbound endpoint (source {SourceEndpointId}): TLS certificate bound to port {Port}.",
                _options.SourceEndpointId, _tlsBinding.Port);
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_cts.Token);
        _logger.LogInformation(
            "WS inbound endpoint (source {SourceEndpointId}) listening on {Prefix}.",
            _options.SourceEndpointId,
            _listenerPrefix);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_listener.IsListening) _listener.Stop();
        if (_acceptLoop is not null)
            try { await _acceptLoop.WaitAsync(cancellationToken); } catch (OperationCanceledException) { }

        if (_tlsBinding is not null)
        {
            _tlsBinding.Unbind();
            _logger.LogInformation(
                "WS inbound endpoint (source {SourceEndpointId}): TLS certificate unbound from port {Port}.",
                _options.SourceEndpointId, _tlsBinding.Port);
        }

        _logger.LogInformation("WS inbound endpoint (source {SourceEndpointId}) stopped.", _options.SourceEndpointId);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext http;
            try { http = await _listener.GetContextAsync().WaitAsync(ct); }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("WS inbound accept loop canceled on source {SourceEndpointId}.", _options.SourceEndpointId);
                break;
            }
            catch (HttpListenerException ex)
            {
                _logger.LogDebug(ex, "WS inbound accept loop ended (source {SourceEndpointId}).", _options.SourceEndpointId);
                break;
            }
            _ = HandleConnectionAsync(http, ct);   // one task per socket
        }
    }

    private async Task HandleConnectionAsync(HttpListenerContext http, CancellationToken ct)
    {
        if (!http.Request.IsWebSocketRequest)
        {
            _logger.LogWarning(
                "WS inbound (source {SourceEndpointId}): rejected non-WebSocket request from {RemoteEndPoint} (HTTP {Method} {Url}) — responded 400.",
                _options.SourceEndpointId, http.Request.RemoteEndPoint, http.Request.HttpMethod, http.Request.Url);
            http.Response.StatusCode = 400;
            http.Response.Close();
            return;
        }

        if (_options.Tls.RequiresClientCertificate() && !await ValidateClientCertificateAsync(http, ct))
        {
            http.Response.StatusCode = 403;
            http.Response.Close();
            return;
        }

        var wsContext = await TryAcceptWebSocketAsync(http);
        if (wsContext is null)
            return;

        await HandleAcceptedConnectionAsync(wsContext.WebSocket, ct);
    }

    private async Task HandleAcceptedConnectionAsync(System.Net.WebSockets.WebSocket socket, CancellationToken ct)
    {
        var writeLock = new SemaphoreSlim(1, 1);
        var duplexSession = RegisterDuplexSessionIfNeeded(socket, writeLock);
        try
        {
            var buffer = new byte[_options.ReceiveBufferSize];
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var (payload, messageType) = await ReceiveMessageAsync(socket, buffer, ct);
                if (payload is null)
                    return;

                if (duplexSession is not null && duplexSession.TryCompletePendingReply(payload))
                    continue;

                await DispatchInboundPayloadAsync(socket, writeLock, duplexSession, payload, messageType, ct);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("WS inbound connection canceled on source {SourceEndpointId}.", _options.SourceEndpointId);
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex,
                "WS inbound peer disconnected on source {SourceEndpointId}.",
                _options.SourceEndpointId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unhandled WS inbound connection error on source {SourceEndpointId}.",
                _options.SourceEndpointId);
        }
        finally
        {
            if (duplexSession is not null)
                _duplexSessions?.Unregister(duplexSession);
            else
                writeLock.Dispose();
            socket.Dispose();
        }
    }

    private async Task<(byte[]? Payload, WebSocketMessageType MessageType)> ReceiveMessageAsync(System.Net.WebSockets.WebSocket socket, byte[] buffer, CancellationToken ct)
    {
        using var acc = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, ct);
                return (null, WebSocketMessageType.Close);
            }
            acc.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return (acc.ToArray(), result.MessageType);
    }

    private async Task DispatchInboundPayloadAsync(
        System.Net.WebSockets.WebSocket socket,
        SemaphoreSlim writeLock,
        WebSocketDuplexSession? duplexSession,
        byte[] payload,
        WebSocketMessageType messageType,
        CancellationToken ct)
    {
        var envelope = messageType == WebSocketMessageType.Text
            ? TransportMessageEnvelope.ParseJson(payload, requireJsonObjectPrefix: false)
            : TransportMessageEnvelope.Raw(payload);

        await _admission.WaitAsync(ct);
        try
        {
            var token = duplexSession?.CreateAckToken() ?? new WebSocketAckToken(socket, writeLock);
            var reply = _replyFactory.Create(_options.SourceEndpointId, token);
            var ctx = new MessageContext(
                correlationId: envelope.CorrelationId ?? Guid.NewGuid().ToString("N"),
                sourceEndpointId: _options.SourceEndpointId,
                format: _options.Format,
                ack: token,
                reply: reply,
                payload: envelope.Payload,
                headers: envelope.Headers);

            ctx.Reply.Attach(ctx);
            await _dispatcher.DispatchAsync(ctx, ct);
        }
        finally
        {
            _admission.Release();
        }
    }

    private async Task<HttpListenerWebSocketContext?> TryAcceptWebSocketAsync(HttpListenerContext http)
    {
        try
        {
            return await http.AcceptWebSocketAsync(subProtocol: null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "WS inbound failed to accept WebSocket request on source {SourceEndpointId}.",
                _options.SourceEndpointId);
            http.Response.StatusCode = 500;
            http.Response.Close();
            return null;
        }
    }

    private WebSocketDuplexSession? RegisterDuplexSessionIfNeeded(System.Net.WebSockets.WebSocket socket, SemaphoreSlim writeLock)
    {
        var duplexSession = _options.Mode == CommunicationMode.DuplexInbound
            ? new WebSocketDuplexSession(_options.SourceEndpointId, socket, writeLock)
            : null;

        if (duplexSession is not null)
            _duplexSessions?.Register(duplexSession);

        return duplexSession;
    }

    // Mutual TLS: http.sys negotiates the client certificate during the TLS handshake
    // (HTTP_SERVICE_CONFIG_SSL_FLAG_NEGOTIATE_CLIENT_CERT is set on the port binding).
    // Validation runs here for a consistent SSL policy with TCP, and hard-rejects:
    //   • missing cert  → 403 (RemoteCertificateNotAvailable)
    //   • chain failure → 403 (RemoteCertificateChainErrors)
    //   • callback deny → 403
    private async Task<bool> ValidateClientCertificateAsync(HttpListenerContext http, CancellationToken ct)
    {
        var clientCert = await http.Request.GetClientCertificateAsync().WaitAsync(ct);
        if (clientCert is null)
        {
            _logger.LogWarning(
                "WS inbound (source {SourceEndpointId}): client certificate required but not presented — connection rejected.",
                _options.SourceEndpointId);
            return false;
        }

        using var chain = new System.Security.Cryptography.X509Certificates.X509Chain();
        var built = chain.Build(clientCert);
        var errors = built ? SslPolicyErrors.None : SslPolicyErrors.RemoteCertificateChainErrors;
        var accepted = _clientCertValidator!(this, clientCert, chain, errors);
        if (!accepted)
            _logger.LogWarning(
                "WS inbound (source {SourceEndpointId}): client certificate validation failed (subject: {Subject}, errors: {Errors}) — connection rejected.",
                _options.SourceEndpointId, clientCert.Subject, errors);
        return accepted;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _cts?.Dispose();
        _tlsBinding?.Dispose();
        _admission.Dispose();
        ((IDisposable)_listener).Dispose();
    }

    private static string NormalizeHttpListenerPrefix(string prefix)
        => prefix.EndsWith("/", StringComparison.Ordinal) ? prefix : prefix + "/";
}
