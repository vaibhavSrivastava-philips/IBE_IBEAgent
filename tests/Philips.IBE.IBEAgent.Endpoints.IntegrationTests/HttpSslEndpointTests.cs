using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Endpoints.Http;
using Philips.IBE.IBEAgent.Security;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class HttpSslEndpointTests
{
    [Fact]
    public void Constructor_throws_when_tls_enabled_but_prefix_is_plain_http()
    {
        var options = new HttpInboundOptions
        {
            SourceEndpointId = 1,
            Prefix = "http://localhost:8080/ibe/",     // not https://
            Tls = new TlsOptions { Mode = TlsMode.OneWay, Certificate = new CertificateReference { Subject = "unused" } },
        };

        Assert.Throws<InvalidOperationException>(
            () => new HttpInboundEndpoint(options, new FakeMessageDispatcher(), new FakeReplyContextFactory()));
    }

    [Fact]
    public async Task Outbound_connects_over_tls_to_a_oneway_ssl_server()
    {
        var certificate = TestCertificateFactory.CreateSelfSigned();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        byte[]? receivedBody = null;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsServerAsync(certificate, clientCertificateRequired: false,
                enabledSslProtocols: SslProtocols.None, checkCertificateRevocation: false);

            receivedBody = await ReadHttpRequestBodyAsync(ssl);

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 6\r\nConnection: close\r\n\r\nMSA|AA"u8.ToArray();
            await ssl.WriteAsync(response);
            await ssl.FlushAsync();
        });

        var options = new HttpOutboundOptions
        {
            Endpoint = new Uri($"https://localhost:{port}/ibe"),
            Tls = new TlsOptions { Mode = TlsMode.OneWay, SkipCertificateValidation = true }, // self-signed test cert
        };
        using var endpoint = new HttpOutboundEndpoint(options, codec: null);

        var result = await endpoint.SendAsync(MessageContextBuilder.Create(payload: "REQ-TLS"), CancellationToken.None);

        await serverTask;
        Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
        Assert.Equal("REQ-TLS", Encoding.UTF8.GetString(receivedBody!));
        listener.Stop();
    }

    [Fact]
    public async Task Outbound_rejects_untrusted_server_certificate_by_default()
    {
        var certificate = TestCertificateFactory.CreateSelfSigned();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            try
            {
                await ssl.AuthenticateAsServerAsync(certificate, clientCertificateRequired: false,
                    enabledSslProtocols: SslProtocols.None, checkCertificateRevocation: false);
            }
            catch { /* client aborted handshake because cert is untrusted, as expected */ }
        });

        var options = new HttpOutboundOptions
        {
            Endpoint = new Uri($"https://localhost:{port}/ibe"),
            Timeout = TimeSpan.FromSeconds(5),
            Tls = new TlsOptions { Mode = TlsMode.OneWay },   // SkipCertificateValidation NOT set -> secure default
        };
        using var endpoint = new HttpOutboundEndpoint(options, codec: null);

        var result = await endpoint.SendAsync(MessageContextBuilder.Create(payload: "x"), CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Failed, result.Outcome);
        listener.Stop();
        await serverTask;
    }

    private static async Task<byte[]> ReadHttpRequestBodyAsync(Stream stream)
    {
        var buffer = new byte[16384];
        var acc = new List<byte>();
        int contentLength = -1;
        int headerEnd = -1;

        while (headerEnd < 0)
        {
            int n = await stream.ReadAsync(buffer);
            if (n == 0) break;
            acc.AddRange(buffer.AsSpan(0, n).ToArray());
            var text = Encoding.ASCII.GetString(acc.ToArray());
            headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd >= 0)
            {
                foreach (var line in text[..headerEnd].Split("\r\n"))
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        contentLength = int.Parse(line["Content-Length:".Length..].Trim());
            }
        }

        int bodyStart = headerEnd + 4;
        while (acc.Count - bodyStart < contentLength)
        {
            int n = await stream.ReadAsync(buffer);
            if (n == 0) break;
            acc.AddRange(buffer.AsSpan(0, n).ToArray());
        }

        return acc.Skip(bodyStart).Take(contentLength).ToArray();
    }
}
