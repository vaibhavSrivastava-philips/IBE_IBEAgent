using System.Collections.Concurrent;
using System.Net.Sockets;
namespace Philips.IBE.IBEAgent.Endpoints.Tcp;

internal sealed class TcpConnectionPool(string host, int port, int size) : IAsyncDisposable
{
    private readonly SemaphoreSlim _slots = new(size, size);
    private readonly ConcurrentQueue<TcpClient> _idle = new();

    public async Task<TcpClient> RentAsync(CancellationToken ct)
    {
        await _slots.WaitAsync(ct);
        if (_idle.TryDequeue(out var c) && c.Connected) return c;
        c?.Dispose();
        var client = new TcpClient();
        await client.ConnectAsync(host, port, ct);
        return client;
    }

    public void Return(TcpClient client)                          // healthy -> reuse
    {
        if (client.Connected) _idle.Enqueue(client); else client.Dispose();
        _slots.Release();
    }

    public void Discard(TcpClient client)                         // broken -> drop
    {
        client.Dispose();
        _slots.Release();
    }

    public ValueTask DisposeAsync()
    {
        while (_idle.TryDequeue(out var c)) c.Dispose();
        _slots.Dispose();
        return ValueTask.CompletedTask;
    }
}