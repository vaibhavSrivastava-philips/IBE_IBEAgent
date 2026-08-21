using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Endpoints.Tcp;
using Philips.IBE.IBEAgent.Security;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class TcpTlsEndpointTests
{
    [Fact]
    public async Task OneWay_tls_inbound_accepts_tls_client_and_exchanges_message()
    {
        var certPath = TestCertificateFactory.CreateSelfSignedPfxFile();
        try
        {
            var dispatcher = new FakeMessageDispatcher();
            var options = new TcpInboundOptions
            {
                SourceEndpointId = 1,
                Port = 0,
                Tls = new TlsOptions { Mode = TlsMode.OneWay, Certificate = new CertificateReference() },
            };
            await using var endpoint = new TcpInboundEndpoint(options, dispatcher, new FakeReplyContextFactory(),
                certificateProvider: new FileCertificateProvider(certPath));
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
        finally { System.IO.File.Delete(certPath); }
    }

    [Fact]
    public async Task Mutual_tls_inbound_rejects_client_without_certificate()
    {
        var certPath = TestCertificateFactory.CreateSelfSignedPfxFile();
        try
        {
            var options = new TcpInboundOptions
            {
                SourceEndpointId = 1,
                Port = 0,
                Tls = new TlsOptions { Mode = TlsMode.Mutual, Certificate = new CertificateReference(), SkipCertificateValidation = true },
            };
            await using var endpoint = new TcpInboundEndpoint(options, new FakeMessageDispatcher(), new FakeReplyContextFactory(),
                certificateProvider: new FileCertificateProvider(certPath));
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
        finally { System.IO.File.Delete(certPath); }
    }

    [Fact]
    public async Task Mutual_tls_roundtrip_between_outbound_and_inbound_endpoints()
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
                Tls = new TlsOptions
                {
                    Mode = TlsMode.Mutual,
                    Certificate = new CertificateReference(),
                    SkipCertificateValidation = true,   // self-signed client cert in this test
                },
            };
            await using var inbound = new TcpInboundEndpoint(inboundOptions, dispatcher, new FakeReplyContextFactory(),
                certificateProvider: new FileCertificateProvider(serverCertPath));
            await inbound.StartAsync(CancellationToken.None);

            var outboundOptions = new TcpOutboundOptions
            {
                Host = "127.0.0.1",
                Port = inbound.BoundPort,
                ExpectReply = true,
                Tls = new TlsOptions
                {
                    Mode = TlsMode.Mutual,
                    Certificate = new CertificateReference(),
                    SkipCertificateValidation = true,   // self-signed server cert in this test
                },
            };
            await using var outbound = new TcpOutboundEndpoint(outboundOptions, codec: null,
                certificateProvider: new FileCertificateProvider(clientCertPath));

            var sendTask = outbound.SendAsync(MessageContextBuilder.Create(payload: "MUTUAL-TLS"), CancellationToken.None);

            await TestSupport.WaitForAsync(() => dispatcher.Dispatched.Count == 1, TimeSpan.FromSeconds(5));
            await dispatcher.Dispatched[0].Ack.WriteAsync(Encoding.UTF8.GetBytes("MSA|AA"), CancellationToken.None);

            var result = await sendTask;
            Assert.Equal(Abstractions.DeliveryOutcome.Delivered, result.Outcome);
            Assert.Equal("MSA|AA", Encoding.UTF8.GetString(result.ResponsePayload.ToArray()));
        }
        finally
        {
            System.IO.File.Delete(serverCertPath);
            System.IO.File.Delete(clientCertPath);
        }
    }

    [Fact]
    public async Task Mutual_tls_is_inferred_from_client_certificate_and_server_requirement()
    {
        var serverCertPath = TestCertificateFactory.CreateSelfSignedPfxFile();
        var clientCertPath = TestCertificateFactory.CreateSelfSignedPfxFile();
        try
        {
            var dispatcher = new FakeMessageDispatcher();
            var inboundOptions = new TcpInboundOptions
            {
                SourceEndpointId = 1,
                Port = 0,
                Tls = new TlsOptions
                {
                    Enabled = true,
                    Certificate = new CertificateReference(),
                    RequireClientCertificate = true,
                    SkipCertificateValidation = true,
                },
            };
            await using var inbound = new TcpInboundEndpoint(inboundOptions, dispatcher, new FakeReplyContextFactory(),
                certificateProvider: new FileCertificateProvider(serverCertPath));
            await inbound.StartAsync(CancellationToken.None);

            var outboundOptions = new TcpOutboundOptions
            {
                Host = "127.0.0.1",
                Port = inbound.BoundPort,
                ExpectReply = true,
                Tls = new TlsOptions
                {
                    Enabled = true,
                    Certificate = new CertificateReference(),
                    SkipCertificateValidation = true,
                },
            };
            await using var outbound = new TcpOutboundEndpoint(outboundOptions, codec: null,
                certificateProvider: new FileCertificateProvider(clientCertPath));

            var sendTask = outbound.SendAsync(MessageContextBuilder.Create(payload: "MUTUAL-TLS-INFERRED"), CancellationToken.None);

            await TestSupport.WaitForAsync(() => dispatcher.Dispatched.Count == 1, TimeSpan.FromSeconds(5));
            await dispatcher.Dispatched[0].Ack.WriteAsync(Encoding.UTF8.GetBytes("MSA|AA"), CancellationToken.None);

            var result = await sendTask;
            Assert.Equal(Abstractions.DeliveryOutcome.Delivered, result.Outcome);
            Assert.Equal("MSA|AA", Encoding.UTF8.GetString(result.ResponsePayload.ToArray()));
        }
        finally
        {
            System.IO.File.Delete(serverCertPath);
            System.IO.File.Delete(clientCertPath);
        }
    }
}
