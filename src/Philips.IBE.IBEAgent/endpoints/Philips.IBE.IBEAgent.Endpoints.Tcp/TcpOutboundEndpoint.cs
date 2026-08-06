// TcpOutboundEndpoint.cs
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Philips.IBE.IBEAgent.Abstractions;
namespace Philips.IBE.IBEAgent.Endpoints.Tcp;

public sealed class TcpOutboundEndpoint : IOutboundEndpoint, IAsyncDisposable
{
    private readonly TcpOutboundOptions _options;
    private readonly IMessageCodec? _codec;
    private readonly ILogger<TcpOutboundEndpoint> _logger;
    private readonly TcpConnectionPool _pool;

    public TcpOutboundEndpoint(TcpOutboundOptions options, IMessageCodec? codec, ILogger<TcpOutboundEndpoint>? logger = null)
    {
        _options = options; _codec = codec;
        _pool = new TcpConnectionPool(options.Host, options.Port, options.PoolSize, options.Ssl, options.Proxy);
        _logger = logger ?? NullLogger<TcpOutboundEndpoint>.Instance;
        _pool = new TcpConnectionPool(options.Host, options.Port, options.PoolSize);
    }

    public async Task<DeliveryResult> SendAsync(MessageContext context, CancellationToken cancellationToken)
    {
        TcpPooledConnection? connection = null;
        var wire = _codec?.Encode(context) ?? context.Payload;   // canonical model -> destination bytes (once; reused across a reconnect)
        var framed = MllpFramer.Frame(wire.Span);

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

    // One delivery attempt; fully owns the rented connection's lifecycle (return on success, discard on
    // failure). `staleRetry` is true only when a REUSED connection failed at the transport (not on a
    // cancelled send) — the signal for the caller to retry once on a freshly-dialed connection.
    private async Task<(DeliveryResult result, bool staleRetry)> TrySendOnceAsync(
        ReadOnlyMemory<byte> framed, MessageContext context, bool forceFresh, CancellationToken cancellationToken)
    {
        TcpClient client;
        bool reused;
        try
        {
            (client, reused) = await _pool.RentAsync(forceFresh, cancellationToken);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            return (new DeliveryResult(DeliveryOutcome.Failed, ex.Message), false);   // dial failed -> downstream unreachable
        }

        bool healthy = false;
        try
        {
            var wire = _codec?.Encode(context) ?? context.Payload;                    // canonical model -> destination bytes
            var framed = MllpFramer.Frame(wire.Span);

            connection = await _pool.RentAsync(cancellationToken);
            var stream = connection.Stream;
            var stream = client.GetStream();
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
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            return (new DeliveryResult(DeliveryOutcome.Failed, ex.Message),
                reused && !forceFresh && !cancellationToken.IsCancellationRequested);
        }
        finally
        {
            if (connection is not null) { if (healthy) _pool.Return(connection); else _pool.Discard(connection); }
            if (healthy) _pool.Return(client); else _pool.Discard(client);
        }
    }

    public ValueTask DisposeAsync() => _pool.DisposeAsync();
}
