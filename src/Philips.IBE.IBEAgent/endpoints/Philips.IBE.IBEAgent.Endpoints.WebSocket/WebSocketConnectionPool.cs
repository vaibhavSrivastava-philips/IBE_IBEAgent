using System.Collections.Concurrent;
using System.Net.WebSockets;
using Philips.IBE.IBEAgent.Security;

namespace Philips.IBE.IBEAgent.Endpoints.WebSocket;

// SRP: WebSocketConnectionPool is responsible ONLY for slot accounting and idle-connection reuse.
// The physical work of configuring SSL/proxy and connecting is delegated to IWebSocketConnectionFactory.
internal sealed class WebSocketConnectionPool : IAsyncDisposable
{
    private readonly SemaphoreSlim _slots;
    private readonly ConcurrentQueue<ClientWebSocket> _idle = new();
    private readonly IWebSocketConnectionFactory _factory;

    // Production constructor: creates the default WebSocketConnectionFactory internally.
    public WebSocketConnectionPool(Uri endpoint, int size, TlsOptions? tls = null, ProxyOptions? proxy = null)
        : this(size, new WebSocketConnectionFactory(endpoint, tls ?? new TlsOptions(), proxy ?? new ProxyOptions()))
    { }

    // DIP constructor: accept any IWebSocketConnectionFactory (e.g. a test double).
    public WebSocketConnectionPool(int size, IWebSocketConnectionFactory factory)
    {
        _slots   = new SemaphoreSlim(size, size);
        _factory = factory;
    }

    public async Task<ClientWebSocket> RentAsync(bool forceFresh, CancellationToken ct)
    {
        await _slots.WaitAsync(ct);

        ClientWebSocket? existing = null;
        if (!forceFresh && _idle.TryDequeue(out existing) && existing.State == WebSocketState.Open)
            return existing;

        existing?.Dispose();

        return await _factory.CreateAsync(ct);
    }

    public void Return(ClientWebSocket socket)          // healthy -> reuse
    {
        if (socket.State == WebSocketState.Open) _idle.Enqueue(socket); else socket.Dispose();
        _slots.Release();
    }

    public void Discard(ClientWebSocket socket)         // broken -> drop
    {
        socket.Dispose();
        _slots.Release();
    }

    public ValueTask DisposeAsync()
    {
        while (_idle.TryDequeue(out var s)) s.Dispose();
        _slots.Dispose();
        return ValueTask.CompletedTask;
    }
}
