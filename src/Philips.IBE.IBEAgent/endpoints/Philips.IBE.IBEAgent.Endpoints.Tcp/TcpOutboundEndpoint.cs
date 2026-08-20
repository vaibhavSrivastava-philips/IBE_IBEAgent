// TcpOutboundEndpoint.cs
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Philips.IBE.IBEAgent.Abstractions;
namespace Philips.IBE.IBEAgent.Endpoints.Tcp;

public sealed class TcpOutboundEndpoint : IOutboundEndpoint, IEndpointLifecycle, IRequiresInboundDispatch, IAsyncDisposable
{
    private readonly TcpOutboundOptions _options;
    private readonly IMessageCodec? _codec;
    private readonly ILogger<TcpOutboundEndpoint> _logger;
    private readonly TcpConnectionPool _pool;
    private readonly ITcpConnectRetryPolicy _connectRetryPolicy;
    private readonly TcpDuplexSessionRegistry? _duplexSessions;
    private readonly SemaphoreSlim _duplexWriteLock = new(1, 1);
    private readonly SemaphoreSlim _duplexConnectGate = new(1, 1);
    private TcpPooledConnection? _duplexConnection;
    private TaskCompletionSource<ReadOnlyMemory<byte>>? _pendingDuplexReply;
    private CancellationTokenSource? _duplexCts;
    private Task? _duplexReader;
    private IMessageDispatcher? _dispatcher;
    private IReplyContextFactory? _replyFactory;

    public TcpOutboundEndpoint(
        TcpOutboundOptions options,
        IMessageCodec? codec,
        ILogger<TcpOutboundEndpoint>? logger = null,
        TcpDuplexSessionRegistry? duplexSessions = null)
        : this(options, codec, logger, duplexSessions, connectRetryPolicy: null) { }

    // Internal constructor used by tests and DI to inject custom retry policy / pool.
    internal TcpOutboundEndpoint(
        TcpOutboundOptions options,
        IMessageCodec? codec,
        ILogger<TcpOutboundEndpoint>? logger,
        TcpDuplexSessionRegistry? duplexSessions,
        ITcpConnectRetryPolicy? connectRetryPolicy)
    {
        _options = options;
        _codec = codec;
        _logger = logger ?? NullLogger<TcpOutboundEndpoint>.Instance;
        _duplexSessions = duplexSessions;
        _connectRetryPolicy = connectRetryPolicy ?? new TcpConnectRetryPolicy();
        _pool = new TcpConnectionPool(options.Host, options.Port,
            options.Mode == CommunicationMode.DuplexOutbound ? 1 : options.PoolSize,
            options.Ssl, options.Proxy);
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
            throw new InvalidOperationException($"TCP DuplexOutbound endpoint {_options.Host}:{_options.Port} requires SourceEndpointId.");
        if (_dispatcher is null || _replyFactory is null)
            throw new InvalidOperationException($"TCP DuplexOutbound endpoint {_options.Host}:{_options.Port} requires inbound dispatch services.");

        _duplexCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _duplexReader = RunDuplexReaderAsync(_duplexCts.Token);
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
            catch (TimeoutException) { }
        }

        var connection = Interlocked.Exchange(ref _duplexConnection, null);
        if (connection is not null) _pool.Discard(connection);
    }

    public async Task<DeliveryResult> SendAsync(MessageContext context, CancellationToken cancellationToken)
    {
        var wire = _codec?.Encode(context) ?? context.Payload;   // canonical model -> destination bytes (once; reused across a reconnect)
        var framed = MllpFramer.Frame(wire.Span);

        if (_options.Mode == CommunicationMode.DuplexOutbound)
            return await SendDuplexOutboundAsync(framed, context, cancellationToken);
        if (_options.Mode == CommunicationMode.DuplexInbound)
            return await SendDuplexInboundAsync(framed, context, cancellationToken);

        // The first attempt may draw a POOLED connection the peer silently closed while idle. If it
        // fails at the transport, reconnect ONCE on a fresh dial. This is duplicate-safe: bytes written
        // to a peer-closed socket are RST'd, never delivered to the downstream application.
        var (result, staleRetry) = await TrySendOnceAsync(framed, context, forceFresh: false, cancellationToken);
        if (!staleRetry) return result;

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Stale pooled connection to {Host}:{Port}; reconnecting and retrying delivery once.", _options.Host, _options.Port);

        (result, _) = await TrySendOnceAsync(framed, context, forceFresh: true, cancellationToken);
        return result;
    }

    private async Task<DeliveryResult> SendDuplexInboundAsync(
        ReadOnlyMemory<byte> framed, MessageContext context, CancellationToken cancellationToken)
    {
        if (_options.DuplexInboundSourceEndpointId is null)
            return new DeliveryResult(DeliveryOutcome.Failed, "DuplexInboundSourceEndpointId is not configured.");

        if (_duplexSessions is null || !_duplexSessions.TryGet(_options.DuplexInboundSourceEndpointId.Value, out var session) || session is null)
            return new DeliveryResult(DeliveryOutcome.Failed, $"No active TCP DuplexInbound session for source {_options.DuplexInboundSourceEndpointId.Value}.");

        return await session.SendAsync(
            framed,
            _options.ExpectReply,
            _options.ReplyCorrelationTimeout,
            context.Format,
            cancellationToken);
    }

    private async Task<DeliveryResult> SendDuplexOutboundAsync(
        ReadOnlyMemory<byte> framed, MessageContext context, CancellationToken cancellationToken)
    {
        TaskCompletionSource<ReadOnlyMemory<byte>>? pending = null;
        if (_options.ExpectReply)
        {
            pending = new TaskCompletionSource<ReadOnlyMemory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var previous = Interlocked.Exchange(ref _pendingDuplexReply, pending);
            previous?.TrySetCanceled(cancellationToken);
        }

        try
        {
            await _duplexWriteLock.WaitAsync(cancellationToken);
            try
            {
                var stream = await GetDuplexStreamAsync(cancellationToken);
                await stream.WriteAsync(framed, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            finally { _duplexWriteLock.Release(); }

            if (pending is null)
                return new DeliveryResult(DeliveryOutcome.Delivered);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.ReplyCorrelationTimeout);
            var replyBytes = await pending.Task.WaitAsync(timeoutCts.Token);
            return new DeliveryResult(DeliveryOutcome.Delivered, ResponsePayload: replyBytes, ResponseFormat: context.Format);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (pending is not null)
                Interlocked.CompareExchange(ref _pendingDuplexReply, null, pending);
            _logger.LogWarning(
                "TCP DuplexOutbound to {Host}:{Port} timed out waiting for reply after {TimeoutMs} ms.",
                _options.Host,
                _options.Port,
                _options.ReplyCorrelationTimeout.TotalMilliseconds);
            return new DeliveryResult(DeliveryOutcome.Failed, "no MLLP ack received");
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or AuthenticationException)
        {
            if (pending is not null)
                Interlocked.CompareExchange(ref _pendingDuplexReply, null, pending);
            _logger.LogWarning(ex,
                "TCP DuplexOutbound send to {Host}:{Port} failed.",
                _options.Host,
                _options.Port);
            return new DeliveryResult(DeliveryOutcome.Failed, ex.Message);
        }
    }

    private async Task<Stream> GetDuplexStreamAsync(CancellationToken cancellationToken)
    {
        var existing = _duplexConnection;
        if (existing is { Connected: true }) return existing.Stream;

        await _duplexConnectGate.WaitAsync(cancellationToken);
        try
        {
            existing = _duplexConnection;
            if (existing is { Connected: true }) return existing.Stream;

            if (existing is not null)
            {
                _pool.Discard(existing);
                _duplexConnection = null;
            }

            var (connection, _) = await _connectRetryPolicy.RentAsync(_pool, _options, _logger, forceFresh: true, cancellationToken);
            _duplexConnection = connection;
            return connection.Stream;
        }
        finally { _duplexConnectGate.Release(); }
    }

    private async Task RunDuplexReaderAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpPooledConnection? connection = null;
            try
            {
                await GetDuplexStreamAsync(cancellationToken);
                connection = _duplexConnection;
                if (connection is null) continue;

                await foreach (var frame in MllpFramer.ReadMessagesAsync(connection.Stream, cancellationToken))
                {
                    var pending = Interlocked.Exchange(ref _pendingDuplexReply, null);
                    if (pending is not null)
                    {
                        pending.TrySetResult(frame);
                        continue;
                    }

                    await DispatchDuplexInboundAsync(frame, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is SocketException or IOException or AuthenticationException)
            {
                _logger.LogDebug(ex, "TCP DuplexOutbound reader disconnected from {Host}:{Port}; reconnecting.", _options.Host, _options.Port);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error in TCP DuplexOutbound reader for {Host}:{Port}; reconnecting.",
                    _options.Host, _options.Port);
            }
            finally
            {
                var current = Interlocked.Exchange(ref _duplexConnection, null);
                if (current is not null) _pool.Discard(current);
                var pending = Interlocked.Exchange(ref _pendingDuplexReply, null);
                pending?.TrySetCanceled(cancellationToken);
            }

            try { await Task.Delay(_options.ReconnectDelay, cancellationToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task DispatchDuplexInboundAsync(byte[] frame, CancellationToken cancellationToken)
    {
        var envelope = TransportMessageEnvelope.ParseJson(frame);
        var stream = _duplexConnection?.Stream ?? throw new IOException("TCP DuplexOutbound connection is not available.");
        var token = new TcpConnectionAckToken(stream, _duplexWriteLock, _logger);
        var reply = _replyFactory!.Create(_options.SourceEndpointId!.Value, token);
        var ctx = new MessageContext(
            correlationId: envelope.CorrelationId ?? Guid.NewGuid().ToString("N"),
            sourceEndpointId: _options.SourceEndpointId.Value,
            format: _options.InboundFormat,
            ack: token,
            reply: reply,
            payload: envelope.Payload,
            headers: envelope.Headers);

        ctx.Reply.Attach(ctx);
        try
        {
            await _dispatcher!.DispatchAsync(ctx, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to dispatch inbound message {CorrelationId} on TCP DuplexOutbound source {SourceEndpointId}.",
                ctx.CorrelationId, _options.SourceEndpointId.Value);
        }
    }

    // One delivery attempt; fully owns the rented connection's lifecycle (return on success, discard on
    // failure). `staleRetry` is true only when a REUSED connection failed at the transport (not on a
    // cancelled send) — the signal for the caller to retry once on a freshly-dialed connection.
    private async Task<(DeliveryResult result, bool staleRetry)> TrySendOnceAsync(
        ReadOnlyMemory<byte> framed, MessageContext context, bool forceFresh, CancellationToken cancellationToken)
    {
        TcpPooledConnection connection;
        bool reused;
        try
        {
            (connection, reused) = await _connectRetryPolicy.RentAsync(_pool, _options, _logger, forceFresh, cancellationToken);
        }
        catch (Exception ex) when (ex is SocketException or IOException or AuthenticationException)
        {
            _logger.LogWarning(ex,
                "TCP outbound connection to {Host}:{Port} failed after {AttemptCount} attempt(s).",
                _options.Host,
                _options.Port,
                Math.Max(1, _options.ConnectRetryCount + 1));
            return (new DeliveryResult(DeliveryOutcome.Failed, ex.Message), false);   // dial failed -> downstream unreachable
        }

        bool healthy = false;
        try
        {
            var stream = connection.Stream;
            await stream.WriteAsync(framed, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            if (_options.ExpectReply)
            {
                await foreach (var replyBytes in MllpFramer.ReadMessagesAsync(stream, cancellationToken))
                {
                    healthy = true;
                    return (new DeliveryResult(DeliveryOutcome.Delivered,
                        ResponsePayload: replyBytes, ResponseFormat: context.Format), false);
                }
                // Stream closed before an ack frame: a stale-socket artifact when the connection was reused.
                return (new DeliveryResult(DeliveryOutcome.Failed, "no MLLP ack received"),
                    reused && !forceFresh && !cancellationToken.IsCancellationRequested);
            }

            healthy = true;
            return (new DeliveryResult(DeliveryOutcome.Delivered), false);
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or AuthenticationException)
        {
            _logger.LogWarning(ex,
                "TCP outbound send to {Host}:{Port} failed (forceFresh={ForceFresh}, reused={Reused}).",
                _options.Host,
                _options.Port,
                forceFresh,
                reused);
            return (new DeliveryResult(DeliveryOutcome.Failed, ex.Message),
                reused && !forceFresh && !cancellationToken.IsCancellationRequested);
        }
        finally
        {
            if (healthy) _pool.Return(connection); else _pool.Discard(connection);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _duplexCts?.Dispose();
        _duplexWriteLock.Dispose();
        _duplexConnectGate.Dispose();
        await _pool.DisposeAsync();
    }
}
