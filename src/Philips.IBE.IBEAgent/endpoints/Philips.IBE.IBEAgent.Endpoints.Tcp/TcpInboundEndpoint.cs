using System.Net;
using System.Net.Sockets;
using Philips.IBE.IBEAgent.Abstractions;
namespace Philips.IBE.IBEAgent.Endpoints.Tcp;

public sealed class TcpInboundEndpoint : IInboundEndpoint, IAsyncDisposable
{
    private readonly TcpInboundOptions _options;
    private readonly IMessageDispatcher _dispatcher;
    private readonly IReplyContextFactory _replyFactory;
    private readonly SemaphoreSlim _admission;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public TcpInboundEndpoint(TcpInboundOptions options, IMessageDispatcher dispatcher, IReplyContextFactory replyFactory)
    {
        _options = options; _dispatcher = dispatcher; _replyFactory = replyFactory;
        _admission = new SemaphoreSlim(options.MaxConcurrentMessages);
    }

    public int BoundPort => ((IPEndPoint)_listener!.LocalEndpoint).Port; // handy for tests (port 0)

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new TcpListener(IPAddress.Loopback, _options.Port);
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _listener?.Stop();
        if (_acceptLoop is not null)
            try { await _acceptLoop.WaitAsync(cancellationToken); } catch (OperationCanceledException) { }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { break; }
            _ = HandleConnectionAsync(client, ct);   // one task per connection
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            var stream = client.GetStream();
            var writeLock = new SemaphoreSlim(1, 1);
            try
            {
                await foreach (var payload in MllpFramer.ReadMessagesAsync(stream, ct))
                {
                    await _admission.WaitAsync(ct);
                    try
                    {
                        var token = new TcpConnectionAckToken(stream, writeLock);
                        var reply = _replyFactory.Create(_options.SourceEndpointId, token);
                        var ctx = new MessageContext(
                            correlationId: Guid.NewGuid().ToString("N"),
                            sourceEndpointId: _options.SourceEndpointId,
                            format: _options.Format,
                            ack: token,
                            reply: reply,
                            payload: payload);
                        await _dispatcher.DispatchAsync(ctx, ct); // backpressure comes from the ingress queue
                    }
                    finally { _admission.Release(); }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }                              // peer reset; connection ends
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _cts?.Dispose();
        _admission.Dispose();
    }
}