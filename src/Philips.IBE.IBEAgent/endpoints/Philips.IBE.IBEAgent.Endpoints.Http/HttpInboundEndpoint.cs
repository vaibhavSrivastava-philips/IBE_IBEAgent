using System.Net;
using Philips.IBE.IBEAgent.Abstractions;
namespace Philips.IBE.IBEAgent.Endpoints.Http;

public sealed class HttpInboundEndpoint : IInboundEndpoint, IAsyncDisposable
{
    private readonly HttpInboundOptions _options;
    private readonly IMessageDispatcher _dispatcher;
    private readonly IReplyContextFactory _replyFactory;
    private readonly SemaphoreSlim _admission;
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public HttpInboundEndpoint(HttpInboundOptions options, IMessageDispatcher dispatcher, IReplyContextFactory replyFactory)
    {
        _options = options; _dispatcher = dispatcher; _replyFactory = replyFactory;
        _admission = new SemaphoreSlim(options.MaxConcurrentRequests);
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
            _ = HandleRequestAsync(http, ct);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext http, CancellationToken ct)
    {
        await _admission.WaitAsync(ct);
        var token = new HttpResponseAckToken(http.Response);
        try
        {
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

            ctx.Reply.Attach(ctx); 
            await _dispatcher.DispatchAsync(ctx, ct);
            await token.Completion.WaitAsync(_options.ReplyTimeout, ct); // hold request until reply/timeout (§6.1)
        }
        catch (TimeoutException) { token.CompleteWithError(504); }
        catch (OperationCanceledException) { token.CompleteWithError(503); }
        catch (Exception) { token.CompleteWithError(500); }
        finally { _admission.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _cts?.Dispose();
        _admission.Dispose();
        ((IDisposable)_listener).Dispose();
    }
}