using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Philips.IBE.IBEAgent.Security;

namespace Philips.IBE.IBEAgent.Endpoints.Tcp;

// SRP: TcpConnectionPool owns slot management / idle queue only.
// Creating the physical connection (dial + TLS + proxy) is the sole responsibility of this interface.
internal interface ITcpConnectionFactory
{
    Task<TcpPooledConnection> CreateAsync(bool forceFresh, CancellationToken ct);
}

// Default production factory: dials TCP, optionally tunnels through an HTTP CONNECT proxy,
// and performs the TLS handshake when configured.
internal sealed class TcpConnectionFactory(
    string host, int port, TlsOptions tls, ProxyOptions proxy,
    ICertificateProvider? certificateProvider = null) : ITcpConnectionFactory
{
    private readonly X509Certificate2? _clientCertificate =
        tls.HasCertificate()
            ? (certificateProvider != null ? tls.LoadCertificate(certificateProvider) : tls.LoadCertificate())
            : null;

    public async Task<TcpPooledConnection> CreateAsync(bool forceFresh, CancellationToken ct)
    {
        var client = new System.Net.Sockets.TcpClient();

        if (proxy.IsEnabled)
        {
            await client.ConnectAsync(proxy.Host!, proxy.Port, ct);
            await ConnectThroughProxyAsync(client.GetStream(), ct);
        }
        else
        {
            await client.ConnectAsync(host, port, ct);
        }

        Stream stream = client.GetStream();

        if (tls.IsEnabled)
        {
            var sslStream = new SslStream(stream, leaveInnerStreamOpen: false,
                tls.CreateRemoteCertificateValidator());

            var clientCertificates = _clientCertificate is not null
                ? new X509CertificateCollection { _clientCertificate }
                : null;

            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                ClientCertificates = clientCertificates,
                EnabledSslProtocols = tls.Protocols,
                CertificateRevocationCheckMode = tls.CheckCertificateRevocation
                    ? X509RevocationMode.Online
                    : X509RevocationMode.NoCheck,
            }, ct);

            stream = sslStream;
        }

        return new TcpPooledConnection(client, stream);
    }

    private async Task ConnectThroughProxyAsync(System.Net.Sockets.NetworkStream proxyStream, CancellationToken ct)
    {
        var target = $"{host}:{port}";
        var sb = new System.Text.StringBuilder()
            .Append("CONNECT ").Append(target).Append(" HTTP/1.1\r\n")
            .Append("Host: ").Append(target).Append("\r\n");

        if (proxy.HasCredentials)
        {
            var credentials = Convert.ToBase64String(
                System.Text.Encoding.ASCII.GetBytes($"{proxy.Username}:{proxy.Password}"));
            sb.Append("Proxy-Authorization: Basic ").Append(credentials).Append("\r\n");
        }

        sb.Append("Proxy-Connection: Keep-Alive\r\n\r\n");

        var requestBytes = System.Text.Encoding.ASCII.GetBytes(sb.ToString());
        await proxyStream.WriteAsync(requestBytes, ct);
        await proxyStream.FlushAsync(ct);

        var statusLine = await ReadProxyResponseHeadersAsync(proxyStream, ct);
        if (!statusLine.Contains(" 200"))
            throw new IOException($"Forward proxy CONNECT to {target} failed: {statusLine}");
    }

    private static async Task<string> ReadProxyResponseHeadersAsync(
        System.Net.Sockets.NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var acc = new List<byte>();
        while (true)
        {
            int n = await stream.ReadAsync(buffer, ct);
            if (n == 0) throw new IOException("Proxy closed connection during CONNECT handshake.");
            acc.AddRange(buffer.AsSpan(0, n).ToArray());

            var text = System.Text.Encoding.ASCII.GetString(acc.ToArray());
            var terminatorIndex = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (terminatorIndex >= 0)
            {
                var headers = text[..terminatorIndex];
                var firstLineEnd = headers.IndexOf("\r\n", StringComparison.Ordinal);
                return firstLineEnd >= 0 ? headers[..firstLineEnd] : headers;
            }
        }
    }
}
