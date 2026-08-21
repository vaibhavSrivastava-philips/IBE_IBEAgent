// WebSocketOutboundEndpoint.cs
using System.Net.WebSockets;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Philips.IBE.IBEAgent.Abstractions;
namespace Philips.IBE.IBEAgent.Endpoints.WebSocket;

public sealed class WebSocketOutboundEndpoint : IOutboundEndpoint, IEndpointLifecycle, IRequiresInboundDispatch, IAsyncDisposable
{
    private readonly WebSocketOutboundOptions _options;
    private readonly IMessageCodec? _codec;
    private readonly ILogger<WebSocketOutboundEndpoint> _logger;
    private readonly WebSocketConnectionPool _pool;
    private readonly IWebSocketConnectRetryPolicy _connectRetryPolicy;
    private readonly WebSocketDuplexSessionRegistry? _duplexSessions;
    private readonly SemaphoreSlim _duplexWriteLock = new(1, 1);
    private readonly SemaphoreSlim _duplexConnectGate = new(1, 1);
    private ClientWebSocket? _duplexSocket;
    private WebSocketDuplexSession? _duplexSession;
    private CancellationTokenSource? _duplexCts;
    private Task? _duplexReader;
    private IMessageDispatcher? _dispatcher;
    private IReplyContextFactory? _replyFactory;

    public WebSocketOutboundEndpoint(
        WebSocketOutboundOptions options,
        IMessageCodec? codec,
        WebSocketDuplexSessionRegistry? duplexSessions = null,
        ILogger<WebSocketOutboundEndpoint>? logger = null)
        : this(options, codec, duplexSessions, logger, connectRetryPolicy: null) { }

    // Internal constructor used by tests and DI to inject a custom retry policy.
    internal WebSocketOutboundEndpoint(
        WebSocketOutboundOptions options,
        IMessageCodec? codec,
        WebSocketDuplexSessionRegistry? duplexSessions,
        ILogger<WebSocketOutboundEndpoint>? logger,
        IWebSocketConnectRetryPolicy? connectRetryPolicy)
    {
        _options = options;
        _codec = codec;
        _duplexSessions = duplexSessions;
        _logger = logger ?? NullLogger<WebSocketOutboundEndpoint>.Instance;
        _connectRetryPolicy = connectRetryPolicy ?? new WebSocketConnectRetryPolicy();
        _pool = new WebSocketConnectionPool(options.Endpoint, options.Mode == CommunicationMode.DuplexOutbound ? 1 : options.PoolSize, options.Tls, options.Proxy);
    }

    public void ConfigureInboundDispatch(IMessageDispatcher dispatcher, IReplyContextFactory replyFactory)
    {
        _dispatcher = dispatcher;
        _replyFactory = replyFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.Mode != CommunicationMode.DuplexOutbound) return Task.CompletedTask;
        if (_options.SourceEndpointId is null)
            throw new InvalidOperationException($"WebSocket DuplexOutbound endpoint {_options.Endpoint} requires SourceEndpointId.");
        if (_dispatcher is null || _replyFactory is null)
            throw new InvalidOperationException($"WebSocket DuplexOutbound endpoint {_options.Endpoint} requires inbound dispatch services.");

        _duplexCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _duplexReader = RunDuplexReaderAsync(_duplexCts.Token);
        _logger.LogInformation("WebSocket outbound duplex endpoint connected workflow started for {Endpoint}.", _options.Endpoint);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_duplexCts is null) return;
        await _duplexCts.CancelAsync();
        if (_duplexReader is not null)
        {
            try { await _duplexReader.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken); }
            catch (OperationCanceledException) { }
            catch (TimeoutException)
            {
                _logger.LogWarning("Timed out waiting for WebSocket duplex reader to stop for {Endpoint}.", _options.Endpoint);
            }
        }

        ResetDuplexConnection();
        _logger.LogInformation("WebSocket outbound duplex endpoint stopped for {Endpoint}.", _options.Endpoint);
    }

    public async Task<DeliveryResult> SendAsync(MessageContext context, CancellationToken cancellationToken)
    {
        var wire = _codec?.Encode(context) ?? context.Payload;

        if (_options.Mode == CommunicationMode.DuplexOutbound)
            return await SendDuplexOutboundAsync(wire, context, cancellationToken);
        if (_options.Mode == CommunicationMode.DuplexInbound)
            return await SendDuplexInboundAsync(wire, context, cancellationToken);

        ClientWebSocket? socket = null;
        bool healthy = false;
        try
        {
            socket = await _connectRetryPolicy.RentAsync(_pool, _options, _logger, forceFresh: false, cancellationToken);
            await socket.SendAsync(wire, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);

            if (_options.ExpectReply)
            {
                var responsePayload = await ReceiveReplyAsync(socket, cancellationToken);
                healthy = true;
                return new DeliveryResult(DeliveryOutcome.Delivered,
                    ResponsePayload: responsePayload, ResponseFormat: context.Format);
            }

            healthy = true;
            return new DeliveryResult(DeliveryOutcome.Delivered);
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or OperationCanceledException or AuthenticationException)
        {
            _logger.LogWarning(ex,
                "WebSocket outbound send to {Endpoint} failed.",
                _options.Endpoint);
            return new DeliveryResult(DeliveryOutcome.Failed, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error sending WebSocket outbound message to {Endpoint}.",
                _options.Endpoint);
            return new DeliveryResult(DeliveryOutcome.Failed, ex.Message);
        }
        finally
        {
            if (socket is not null) { if (healthy) _pool.Return(socket); else _pool.Discard(socket); }
        }
    }

    private async Task<DeliveryResult> SendDuplexInboundAsync(ReadOnlyMemory<byte> wire, MessageContext context, CancellationToken cancellationToken)
    {
        if (_options.DuplexInboundSourceEndpointId is null)
            return new DeliveryResult(DeliveryOutcome.Failed, "DuplexInboundSourceEndpointId is not configured.");

        if (_duplexSessions is null || !_duplexSessions.TryGet(_options.DuplexInboundSourceEndpointId.Value, out var session) || session is null)
            return new DeliveryResult(DeliveryOutcome.Failed, $"No active WebSocket DuplexInbound session for source {_options.DuplexInboundSourceEndpointId.Value}.");

        return await session.SendAsync(wire, _options.ExpectReply, _options.ReplyCorrelationTimeout, context.Format, cancellationToken);
    }

    private async Task<DeliveryResult> SendDuplexOutboundAsync(ReadOnlyMemory<byte> wire, MessageContext context, CancellationToken cancellationToken)
    {
        var session = await GetDuplexSessionAsync(cancellationToken);
        return await session.SendAsync(wire, _options.ExpectReply, _options.ReplyCorrelationTimeout, context.Format, cancellationToken);
    }

    private async Task<WebSocketDuplexSession> GetDuplexSessionAsync(CancellationToken cancellationToken)
    {
        var existing = _duplexSession;
        if (existing is not null && existing.Socket.State == WebSocketState.Open) return existing;

        await _duplexConnectGate.WaitAsync(cancellationToken);
        try
        {
            existing = _duplexSession;
            if (existing is not null && existing.Socket.State == WebSocketState.Open) return existing;

            ResetDuplexConnection();

            var socket = await _connectRetryPolicy.RentAsync(_pool, _options, _logger, forceFresh: true, cancellationToken);
            _duplexSocket = socket;
            var session = new WebSocketDuplexSession(_options.SourceEndpointId!.Value, socket, _duplexWriteLock, ownsWriteLock: false);
            _duplexSession = session;
            return session;
        }
        finally { _duplexConnectGate.Release(); }
    }

    private async Task RunDuplexReaderAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var session = await GetDuplexSessionAsync(cancellationToken);
                await ProcessDuplexInboundMessagesAsync(session, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("WebSocket DuplexOutbound reader canceled for endpoint {Endpoint}.", _options.Endpoint);
                break;
            }
            catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException)
            {
                // Expected: peer closed, network drop, or local dispose during shutdown.
                if (!cancellationToken.IsCancellationRequested)
                    _logger.LogDebug(ex, "WebSocket DuplexOutbound reader disconnected from {Endpoint}; will reconnect.", _options.Endpoint);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Unexpected error in WebSocket DuplexOutbound reader for {Endpoint}; reconnecting.",
                    _options.Endpoint);
            }
            finally
            {
                ResetDuplexConnection();
            }

            try { await Task.Delay(_options.ReconnectDelay, cancellationToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task DispatchDuplexInboundAsync(WebSocketDuplexSession session, byte[] payload, WebSocketMessageType messageType, CancellationToken cancellationToken)
    {
        if (_replyFactory is null || _dispatcher is null || _options.SourceEndpointId is null)
            throw new InvalidOperationException($"WebSocket DuplexOutbound endpoint {_options.Endpoint} is missing required inbound dispatch configuration.");

        var envelope = messageType == WebSocketMessageType.Text
            ? TransportMessageEnvelope.ParseJson(payload, requireJsonObjectPrefix: false)
            : TransportMessageEnvelope.Raw(payload);
        var token = session.CreateAckToken();
        var reply = _replyFactory.Create(_options.SourceEndpointId.Value, token);
        var ctx = new MessageContext(
            correlationId: envelope.CorrelationId ?? Guid.NewGuid().ToString("N"),
            sourceEndpointId: _options.SourceEndpointId.Value,
            format: _options.InboundFormat,
            ack: token,
            reply: reply,
            payload: envelope.Payload,
            headers: envelope.Headers);

        ctx.Reply.Attach(ctx);
        await _dispatcher.DispatchAsync(ctx, cancellationToken);
    }

    private async Task<byte[]> ReceiveReplyAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using var acc = new MemoryStream();
        var buffer = new byte[_options.ReceiveBufferSize];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("peer closed before reply");
            acc.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return acc.ToArray();
    }

    private async Task ProcessDuplexInboundMessagesAsync(WebSocketDuplexSession session, CancellationToken cancellationToken)
    {
        var buffer = new byte[_options.ReceiveBufferSize];

        while (!cancellationToken.IsCancellationRequested && session.Socket.State == WebSocketState.Open)
        {
            using var acc = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await session.Socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogDebug("WebSocket DuplexOutbound: peer closed session gracefully for {Endpoint}.", _options.Endpoint);
                    return;   // graceful close — exit cleanly, outer loop will reconnect if needed
                }
                acc.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            var payload = acc.ToArray();
            if (session.TryCompletePendingReply(payload))
                continue;

            await DispatchDuplexInboundAsync(session, payload, result.MessageType, cancellationToken);
        }
    }

    private void ResetDuplexConnection()
    {
        Interlocked.Exchange(ref _duplexSession, null)?.Dispose();
        var socket = Interlocked.Exchange(ref _duplexSocket, null);
        if (socket is not null)
            _pool.Discard(socket);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _duplexCts?.Dispose();
        _duplexConnectGate.Dispose();
        _duplexWriteLock.Dispose();
        await _pool.DisposeAsync();
    }
}
