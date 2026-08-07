using System.Collections.Concurrent;
using System.Net.Sockets;
namespace Philips.IBE.IBEAgent.Endpoints.Tcp;

internal sealed class TcpConnectionPool(string host, int port, int size) : IAsyncDisposable
{
    private readonly SemaphoreSlim _slots = new(size, size);
    private readonly ConcurrentQueue<TcpClient> _idle = new();

    // Returns a connection plus whether it was REUSED from the pool. A transport failure on a reused
    // connection is a likely stale-socket artifact (the peer closed it while idle — TcpClient.Connected
    // can't detect that), which the caller retries once with forceFresh; a freshly-dialed connection
    // failing is a genuine downstream error.
    public async Task<(TcpClient client, bool reused)> RentAsync(bool forceFresh, CancellationToken ct)
    {
        await _slots.WaitAsync(ct);
        try
        {
            if (!forceFresh && _idle.TryDequeue(out var pooled))
            {
                if (pooled.Connected) return (pooled, true);
                pooled.Dispose();                            // obviously-dead idle connection; fall through to a fresh dial
            }
            var client = new TcpClient { NoDelay = true };   // disable Nagle: MLLP request-reply else stalls ~40ms/msg (Nagle + delayed-ACK)
            await client.ConnectAsync(host, port, ct);
            return (client, false);
        }
        catch
        {
            _slots.Release();                                // dial/connect (or cancellation) failed before returning a client: don't leak the slot
            throw;
        }
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