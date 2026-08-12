using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Text;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Endpoints.WebSocket;
using Philips.IBE.IBEAgent.Security;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class WebSocketSslEndpointTests
{
    [Fact]
    public void Constructor_throws_when_ssl_enabled_but_prefix_is_plain_http()
    {
        var options = new WebSocketInboundOptions
        {
            SourceEndpointId = 1,
            Prefix = "http://localhost:8080/ws/",     // not https://
            Ssl = new SslOptions { Mode = SslMode.OneWay, CertificatePath = "unused.pfx" },
        };

        Assert.Throws<InvalidOperationException>(
            () => new WebSocketInboundEndpoint(options, new FakeMessageDispatcher(), new FakeReplyContextFactory()));
    }

    [Fact]
    public async Task Outbound_connects_over_tls_to_a_oneway_ssl_server_and_exchanges_message()
    {
        var certificate = TestCertificateFactory.CreateSelfSigned();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        byte[]? receivedRequest = null;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsServerAsync(certificate, clientCertificateRequired: false,
                enabledSslProtocols: SslProtocols.None, checkCertificateRevocation: false);

            // Minimal WebSocket handshake over the TLS stream.
            var request = await ReadHttpRequestHeadersAsync(ssl);
            var key = ExtractHeader(request, "Sec-WebSocket-Key");
            var accept = Convert.ToBase64String(System.Security.Cryptography.SHA1.HashData(
                Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            var response =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
            await ssl.WriteAsync(Encoding.ASCII.GetBytes(response));
            await ssl.FlushAsync();

            using var socket = System.Net.WebSockets.WebSocket.CreateFromStream(ssl, isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.FromSeconds(30));
            var buffer = new byte[8192];
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            receivedRequest = buffer[..result.Count];
            await socket.SendAsync("MSA|AA"u8.ToArray(), WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);
        });

        var options = new WebSocketOutboundOptions
        {
            Endpoint = new Uri($"wss://localhost:{port}/ws/"),
            Ssl = new SslOptions { Mode = SslMode.OneWay, AllowUntrustedCertificate = true }, // self-signed test cert
        };
        await using var endpoint = new WebSocketOutboundEndpoint(options, codec: null);

        var result = await endpoint.SendAsync(MessageContextBuilder.Create(payload: "REQ-WSS-TLS"), CancellationToken.None);

        await serverTask;
        listener.Stop();

        Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
        Assert.Equal("REQ-WSS-TLS", Encoding.UTF8.GetString(receivedRequest!));
        Assert.Equal("MSA|AA", Encoding.UTF8.GetString(result.ResponsePayload.ToArray()));
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

        var options = new WebSocketOutboundOptions
        {
            Endpoint = new Uri($"wss://localhost:{port}/ws/"),
            Ssl = new SslOptions { Mode = SslMode.OneWay },   // AllowUntrustedCertificate NOT set -> secure default
        };
        await using var endpoint = new WebSocketOutboundEndpoint(options, codec: null);

        var result = await endpoint.SendAsync(MessageContextBuilder.Create(payload: "x"), CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Failed, result.Outcome);
        listener.Stop();
        await serverTask;
    }

    private static async Task<string> ReadHttpRequestHeadersAsync(Stream stream)
    {
        var buffer = new byte[16384];
        var acc = new List<byte>();
        while (true)
        {
            int n = await stream.ReadAsync(buffer);
            if (n == 0) break;
            acc.AddRange(buffer.AsSpan(0, n).ToArray());
            var text = Encoding.ASCII.GetString(acc.ToArray());
            var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end >= 0) return text[..end];
        }
        throw new IOException("connection closed before headers completed");
    }

    private static string ExtractHeader(string headerText, string name)
    {
        foreach (var line in headerText.Split("\r\n"))
            if (line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
                return line[(name.Length + 1)..].Trim();
        throw new InvalidOperationException($"Header '{name}' not found.");
    }
}
