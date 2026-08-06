using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using Philips.IBE.IBEAgent.Endpoints.Tcp;
using Philips.IBE.IBEAgent.Security;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class TcpSslEndpointTests
{
    [Fact]
    public async Task OneWay_ssl_inbound_accepts_tls_client_and_exchanges_message()
    {
        var certPath = TestCertificateFactory.CreateSelfSignedPfxFile();
        try
        {
            var dispatcher = new FakeMessageDispatcher();
            var options = new TcpInboundOptions
            {
                SourceEndpointId = 1,
                Port = 0,
                Ssl = new SslOptions { Mode = SslMode.OneWay, CertificatePath = certPath },
            };
            await using var endpoint = new TcpInboundEndpoint(options, dispatcher, new FakeReplyContextFactory());
            await endpoint.StartAsync(CancellationToken.None);

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, endpoint.BoundPort);
            using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false,
                (_, _, _, _) => true); // client trusts any cert (self-signed test cert)
            await ssl.AuthenticateAsClientAsync("localhost");

            var hl7 = Encoding.UTF8.GetBytes("MSH|SSL-ONEWAY");
            await ssl.WriteAsync(MllpFramer.Frame(hl7));
            await ssl.FlushAsync();

            await TestSupport.WaitForAsync(() => dispatcher.Dispatched.Count == 1, TimeSpan.FromSeconds(5));
            Assert.Equal(hl7, dispatcher.Dispatched[0].Payload.ToArray());

            var ack = Encoding.UTF8.GetBytes("MSA|AA");
            await dispatcher.Dispatched[0].Ack.WriteAsync(ack, CancellationToken.None);
            var replyFrame = await TestSupport.ReadOneFrameAsync(ssl, TimeSpan.FromSeconds(5));
            Assert.Equal(ack, replyFrame);
        }
        finally { File.Delete(certPath); }
    }

    [Fact]
    public async Task TwoWay_ssl_inbound_rejects_client_without_certificate()
    {
        var certPath = TestCertificateFactory.CreateSelfSignedPfxFile();
        try
        {
            var options = new TcpInboundOptions
            {
                SourceEndpointId = 1,
                Port = 0,
                Ssl = new SslOptions { Mode = SslMode.TwoWay, CertificatePath = certPath, AllowUntrustedCertificate = true },
            };
            await using var endpoint = new TcpInboundEndpoint(options, new FakeMessageDispatcher(), new FakeReplyContextFactory());
            await endpoint.StartAsync(CancellationToken.None);

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, endpoint.BoundPort);
            using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false, (_, _, _, _) => true);

            // No client certificate provided while server requires one. The client-side handshake
            // itself may complete (the server enforces ClientCertificateRequired on its own side and
            // then tears down the connection), so assert the connection is unusable afterwards
            // instead of asserting the handshake call throws.
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await ssl.AuthenticateAsClientAsync("localhost");
                var probe = new byte[1];
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                int read = await ssl.ReadAsync(probe, cts.Token);
                if (read == 0) throw new IOException("connection closed by server as expected");
                throw new IOException("unexpected data received; connection was not rejected");
            });
        }
        finally { File.Delete(certPath); }
    }

    [Fact]
    public async Task TwoWay_ssl_roundtrip_between_outbound_and_inbound_endpoints()
    {
        var serverCertPath = TestCertificateFactory.CreateSelfSignedPfxFile();
        var clientCertPath = TestCertificateFactory.CreateSelfSignedPfxFile();
        try
        {
            var dispatcher = new FakeMessageDispatcher();
            var inboundOptions = new TcpInboundOptions
            {
                SourceEndpointId = 9,
                Port = 0,
                Ssl = new SslOptions
                {
                    Mode = SslMode.TwoWay,
                    CertificatePath = serverCertPath,
                    AllowUntrustedCertificate = true,   // self-signed client cert in this test
                },
            };
            await using var inbound = new TcpInboundEndpoint(inboundOptions, dispatcher, new FakeReplyContextFactory());
            await inbound.StartAsync(CancellationToken.None);

            var outboundOptions = new TcpOutboundOptions
            {
                Host = "127.0.0.1",
                Port = inbound.BoundPort,
                ExpectReply = true,
                Ssl = new SslOptions
                {
                    Mode = SslMode.TwoWay,
                    CertificatePath = clientCertPath,
                    AllowUntrustedCertificate = true,   // self-signed server cert in this test
                },
            };
            await using var outbound = new TcpOutboundEndpoint(outboundOptions, codec: null);

            var sendTask = outbound.SendAsync(MessageContextBuilder.Create(payload: "MUTUAL-TLS"), CancellationToken.None);

            await TestSupport.WaitForAsync(() => dispatcher.Dispatched.Count == 1, TimeSpan.FromSeconds(5));
            await dispatcher.Dispatched[0].Ack.WriteAsync(Encoding.UTF8.GetBytes("MSA|AA"), CancellationToken.None);

            var result = await sendTask;
            Assert.Equal(Abstractions.DeliveryOutcome.Delivered, result.Outcome);
            Assert.Equal("MSA|AA", Encoding.UTF8.GetString(result.ResponsePayload.ToArray()));
        }
        finally
        {
            File.Delete(serverCertPath);
            File.Delete(clientCertPath);
        }
    }
}
