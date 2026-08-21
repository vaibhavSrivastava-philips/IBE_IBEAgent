using System.Collections.Concurrent;
using Philips.IBE.IBEAgent.Security;

namespace Philips.IBE.IBEAgent.Endpoints.Tcp;

// A rented connection: the raw socket plus the stream to actually read/write (NetworkStream, or an
// SslStream layered on top of it once the TLS handshake completes).
internal sealed class TcpPooledConnection(System.Net.Sockets.TcpClient client, Stream stream) : IDisposable
{
    public System.Net.Sockets.TcpClient Client { get; } = client;
    public Stream Stream { get; } = stream;
    public bool Connected => Client.Connected;

    public void Dispose()
    {
        Stream.Dispose();
        Client.Dispose();
    }
}

// SRP: TcpConnectionPool is responsible ONLY for slot accounting and idle-connection reuse.
// The physical work of dialing, TLS, and proxy tunneling is delegated to ITcpConnectionFactory.
internal sealed class TcpConnectionPool : IAsyncDisposable
{
    private readonly SemaphoreSlim _slots;
    private readonly ConcurrentQueue<TcpPooledConnection> _idle = new();
    private readonly ITcpConnectionFactory _factory;

    // Production constructor: creates the default TcpConnectionFactory internally.
    public TcpConnectionPool(string host, int port, int size, TlsOptions? tls = null, ProxyOptions? proxy = null, ICertificateProvider? certificateProvider = null)
        : this(size, new TcpConnectionFactory(host, port, tls ?? new TlsOptions(), proxy ?? new ProxyOptions(), certificateProvider))
    { }

    // DIP constructor: accept any ITcpConnectionFactory (e.g. a test double).
    public TcpConnectionPool(int size, ITcpConnectionFactory factory)
    {
        _slots   = new SemaphoreSlim(size, size);
        _factory = factory;
    }

    public async Task<(TcpPooledConnection connection, bool reused)> RentAsync(bool forceFresh, CancellationToken ct)
    {
        await _slots.WaitAsync(ct);

        TcpPooledConnection? pooled = null;
        if (!forceFresh && _idle.TryDequeue(out pooled) && pooled.Connected)
            return (pooled, true);

        pooled?.Dispose();

        var connection = await _factory.CreateAsync(forceFresh, ct);
        return (connection, false);
    }

    public void Return(TcpPooledConnection connection)          // healthy -> reuse
    {
        if (connection.Connected) _idle.Enqueue(connection); else connection.Dispose();
        _slots.Release();
    }

    public void Discard(TcpPooledConnection connection)         // broken -> drop
    {
        connection.Dispose();
        _slots.Release();
    }

    public ValueTask DisposeAsync()
    {
        while (_idle.TryDequeue(out var c)) c.Dispose();
        _slots.Dispose();
        return ValueTask.CompletedTask;
    }
}
