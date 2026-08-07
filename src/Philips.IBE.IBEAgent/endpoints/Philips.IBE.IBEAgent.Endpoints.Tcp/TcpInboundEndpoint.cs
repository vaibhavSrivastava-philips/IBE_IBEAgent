using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Philips.IBE.IBEAgent.Abstractions;
namespace Philips.IBE.IBEAgent.Endpoints.Tcp;

public sealed class TcpInboundEndpoint : IInboundEndpoint, IAsyncDisposable
{
    private readonly TcpInboundOptions _options;
    private readonly IMessageDispatcher _dispatcher;
    private readonly IReplyContextFactory _replyFactory;
    private readonly SemaphoreSlim _admission;
    private readonly ILogger<TcpInboundEndpoint> _logger;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public TcpInboundEndpoint(TcpInboundOptions options, IMessageDispatcher dispatcher, IReplyContextFactory replyFactory, ILogger<TcpInboundEndpoint>? logger = null)
    {
        _options = options; _dispatcher = dispatcher; _replyFactory = replyFactory;
        _admission = new SemaphoreSlim(options.MaxConcurrentMessages);
        _logger = logger ?? NullLogger<TcpInboundEndpoint>.Instance;
    }

    public int BoundPort => ((IPEndPoint)_listener!.LocalEndpoint).Port; // handy for tests (port 0)

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new TcpListener(IPAddress.Loopback, _options.Port);
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_cts.Token);
        _logger.LogInformation(
            "TCP inbound endpoint (source {SourceEndpointId}) listening on port {Port}.",
            _options.SourceEndpointId, BoundPort);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _listener?.Stop();
        if (_acceptLoop is not null)
            try { await _acceptLoop.WaitAsync(cancellationToken); } catch (OperationCanceledException) { }
        _logger.LogInformation("TCP inbound endpoint (source {SourceEndpointId}) stopped.", _options.SourceEndpointId);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }                 // expected on shutdown
            catch (SocketException ex)
            {
                _logger.LogDebug(ex, "TCP inbound accept loop (source {SourceEndpointId}) ended.", _options.SourceEndpointId);
                break;
            }
            _ = HandleConnectionAsync(client, ct);   // one task per connection
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            client.NoDelay = true;   // disable Nagle: the MLLP ack back to the source else stalls ~40ms (Nagle + delayed-ACK)
            var stream = client.GetStream();
            var writeLock = new SemaphoreSlim(1, 1);
            try
            {
                await foreach (var payload in MllpFramer.ReadMessagesAsync(stream, ct))
                {
                    await _admission.WaitAsync(ct);
                    try
                    {
                        var token = new TcpConnectionAckToken(stream, writeLock, _logger);
                        var reply = _replyFactory.Create(_options.SourceEndpointId, token);
                        var ctx = new MessageContext(
                            correlationId: Guid.NewGuid().ToString("N"),
                            sourceEndpointId: _options.SourceEndpointId,
                            format: _options.Format,
                            ack: token,
                            reply: reply,
                            payload: payload);

                        // Monitoring (Information) — per-message receipt for the production flow.
                        _logger.LogInformation(
                            "Received message {CorrelationId} ({ByteCount} bytes) on TCP source {SourceEndpointId}.",
                            ctx.CorrelationId, payload.Length, _options.SourceEndpointId);

                        // Deepest level (Trace) — the full inbound message body. Guarded so the decode
                        // only runs when Trace is enabled (zero cost in production / high-fidelity).
                        if (_logger.IsEnabled(LogLevel.Trace))
                            _logger.LogTrace(
                                "Inbound message {CorrelationId} body: {Message}",
                                ctx.CorrelationId, MessagePreview.ForLog(ctx.Payload.Span));

                        ctx.Reply.Attach(ctx);
                        await _dispatcher.DispatchAsync(ctx, ct); // backpressure comes from the ingress queue
                    }
                    finally { _admission.Release(); }
                }
            }
            catch (OperationCanceledException) { }               // expected on shutdown
            catch (IOException) { }                              // peer reset; connection ends
            catch (Exception ex)
            {
                // A routing/dispatch failure (e.g. no contract for the source) would otherwise become an
                // unobserved task exception since the connection runs fire-and-forget — log it at the boundary.
                _logger.LogError(ex,
                    "Unhandled error processing TCP connection on source {SourceEndpointId}; connection closed.",
                    _options.SourceEndpointId);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _cts?.Dispose();
        _admission.Dispose();
    }
}