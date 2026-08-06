using System.Net;
using System.Net.Security;
using System.Net.WebSockets;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Security;
namespace Philips.IBE.IBEAgent.Endpoints.WebSocket;

public sealed class WebSocketInboundEndpoint : IInboundEndpoint, IAsyncDisposable
{
    private readonly WebSocketInboundOptions _options;
    private readonly IMessageDispatcher _dispatcher;
    private readonly IReplyContextFactory _replyFactory;
    private readonly SemaphoreSlim _admission;
    private readonly HttpListener _listener = new();
    private readonly RemoteCertificateValidationCallback? _clientCertValidator;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public WebSocketInboundEndpoint(WebSocketInboundOptions options, IMessageDispatcher dispatcher, IReplyContextFactory replyFactory)
    {
        _options = options; _dispatcher = dispatcher; _replyFactory = replyFactory;
        _admission = new SemaphoreSlim(options.MaxConcurrentMessages);

        if (options.Ssl.IsEnabled && !options.Prefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"WebSocket inbound endpoint has SSL mode {options.Ssl.Mode} configured but Prefix '{options.Prefix}' is not https://. " +
                "The server certificate itself must be bound to the port out-of-process (e.g. via netsh http add sslcert); " +
                "clients then connect with wss://.");

        if (options.Ssl.RequiresRemoteCertificate)
            _clientCertValidator = options.Ssl.CreateRemoteCertificateValidator();

        _listener.Prefixes.Add(options.Prefix);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_listener.IsListening) _listener.Stop();
        if (_acceptLoop is not null)
            try { await _acceptLoop.WaitAsync(cancellationToken); } catch (OperationCanceledException) { }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext http;
            try { http = await _listener.GetContextAsync().WaitAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }
            _ = HandleConnectionAsync(http, ct);   // one task per socket
        }
    }

    private async Task HandleConnectionAsync(HttpListenerContext http, CancellationToken ct)
    {
        if (!http.Request.IsWebSocketRequest) { http.Response.StatusCode = 400; http.Response.Close(); return; }

        if (_options.Ssl.RequiresRemoteCertificate && !await ValidateClientCertificateAsync(http, ct))
        {
            http.Response.StatusCode = 403;
            http.Response.Close();
            return;
        }

        HttpListenerWebSocketContext wsContext;
        try { wsContext = await http.AcceptWebSocketAsync(subProtocol: null); }
        catch (Exception) { http.Response.StatusCode = 500; http.Response.Close(); return; }

        var socket = wsContext.WebSocket;
        var writeLock = new SemaphoreSlim(1, 1);
        try
        {
            var buffer = new byte[_options.ReceiveBufferSize];
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var acc = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, ct);
                        return;
                    }
                    acc.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                await _admission.WaitAsync(ct);
                try
                {
                    var token = new WebSocketAckToken(socket, writeLock);
                    var reply = _replyFactory.Create(_options.SourceEndpointId, token);
                    var ctx = new MessageContext(
                        correlationId: Guid.NewGuid().ToString("N"),
                        sourceEndpointId: _options.SourceEndpointId,
                        format: _options.Format,
                        ack: token,
                        reply: reply,
                        payload: acc.ToArray());

                    ctx.Reply.Attach(ctx);
                    await _dispatcher.DispatchAsync(ctx, ct); // backpressure comes from the ingress queue
                }
                finally { _admission.Release(); }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }                          // peer reset; connection ends
        finally { socket.Dispose(); }
    }

    // TwoWay mode: HttpListener performs the TLS handshake itself (certificate bound to the port at
    // the OS level), but client-certificate *validation* still runs here via the same
    // RemoteCertificateValidationCallback shape used by TCP/HTTP, for a consistent SSL policy.
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
