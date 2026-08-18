using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Philips.IBE.IBEAgent.Security;
namespace Philips.IBE.IBEAgent.Endpoints.Tcp;

// A rented connection: the raw socket plus the stream to actually read/write (NetworkStream, or an
// SslStream layered on top of it once the TLS handshake completes).
internal sealed class TcpPooledConnection(TcpClient client, Stream stream) : IDisposable
{
    public TcpClient Client { get; } = client;
    public Stream Stream { get; } = stream;
    public bool Connected => Client.Connected;

    public void Dispose()
    {
        Stream.Dispose();
        Client.Dispose();
    }
}

internal sealed class TcpConnectionPool(string host, int port, int size, SslOptions? ssl = null, ProxyOptions? proxy = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _slots = new(size, size);
    private readonly ConcurrentQueue<TcpPooledConnection> _idle = new();
    private readonly SslOptions _ssl = ssl ?? new SslOptions();
    private readonly ProxyOptions _proxy = proxy ?? new ProxyOptions();
    private readonly X509Certificate2? _clientCertificate = ssl?.HasLocalCertificate() == true ? ssl.LoadLocalCertificate() : null;

    public async Task<(TcpPooledConnection connection, bool reused)> RentAsync(bool forceFresh, CancellationToken ct)
    {
        await _slots.WaitAsync(ct);
        TcpPooledConnection? pooled = null;
        if (!forceFresh && _idle.TryDequeue(out pooled) && pooled.Connected) return (pooled, true);
        pooled?.Dispose();

        var client = new TcpClient();

        if (_proxy.IsEnabled)
        {
            await client.ConnectAsync(_proxy.Host!, _proxy.Port, ct);
            await ConnectThroughProxyAsync(client.GetStream(), ct);
        }
        else
        {
            await client.ConnectAsync(host, port, ct);
        }

        Stream stream = client.GetStream();
        if (_ssl.IsEnabled)
        {
            var sslStream = new SslStream(stream, leaveInnerStreamOpen: false, _ssl.CreateRemoteCertificateValidator());
            var clientCertificates = _clientCertificate is not null
                ? new X509CertificateCollection { _clientCertificate }
                : null;

            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                ClientCertificates = clientCertificates,
                EnabledSslProtocols = _ssl.Protocols,
                CertificateRevocationCheckMode = _ssl.CheckCertificateRevocation
                    ? X509RevocationMode.Online
                    : X509RevocationMode.NoCheck,
            }, ct);
            stream = sslStream;
        }

        return (new TcpPooledConnection(client, stream), false);
    }

    // Forward proxy support via the standard HTTP CONNECT tunnel (RFC 7231 §4.3.6), used to reach
    // the real destination host:port through an intermediary. Optional Basic auth when credentials
    // are configured.
    private async Task ConnectThroughProxyAsync(NetworkStream proxyStream, CancellationToken ct)
    {
        var target = $"{host}:{port}";
        var request = new StringBuilder()
            .Append("CONNECT ").Append(target).Append(" HTTP/1.1\r\n")
            .Append("Host: ").Append(target).Append("\r\n");

        if (_proxy.HasCredentials)
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_proxy.Username}:{_proxy.Password}"));
            request.Append("Proxy-Authorization: Basic ").Append(credentials).Append("\r\n");
        }

        request.Append("Proxy-Connection: Keep-Alive\r\n\r\n");

        var requestBytes = Encoding.ASCII.GetBytes(request.ToString());
        await proxyStream.WriteAsync(requestBytes, ct);
        await proxyStream.FlushAsync(ct);

        var statusLine = await ReadProxyResponseHeadersAsync(proxyStream, ct);
        if (!statusLine.Contains(" 200"))
            throw new IOException($"Forward proxy CONNECT to {target} failed: {statusLine}");
    }

    private static async Task<string> ReadProxyResponseHeadersAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var acc = new List<byte>();
        while (true)
        {
            int n = await stream.ReadAsync(buffer, ct);
            if (n == 0) throw new IOException("Proxy closed connection during CONNECT handshake.");
            acc.AddRange(buffer.AsSpan(0, n).ToArray());

            var text = Encoding.ASCII.GetString(acc.ToArray());
            var terminatorIndex = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (terminatorIndex >= 0)
            {
                var headers = text[..terminatorIndex];
                var firstLineEnd = headers.IndexOf("\r\n", StringComparison.Ordinal);
                return firstLineEnd >= 0 ? headers[..firstLineEnd] : headers;
            }
        }
    }

    public void Return(TcpPooledConnection connection)                          // healthy -> reuse
    {
        if (connection.Connected) _idle.Enqueue(connection); else connection.Dispose();
        _slots.Release();
    }

    public void Discard(TcpPooledConnection connection)                         // broken -> drop
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
