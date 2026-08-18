using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Endpoints.Tcp;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class TcpOutboundEndpointTests
{
    [Fact]
    public async Task Sends_framed_message_and_returns_delivered_with_response()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var ack = Encoding.UTF8.GetBytes("MSA|AA");
        byte[]? received = null;

        var server = Task.Run(async () =>
        {
            using var c = await listener.AcceptTcpClientAsync();
            var s = c.GetStream();
            await foreach (var msg in MllpFramer.ReadMessagesAsync(s, CancellationToken.None))
            {
                received = msg;
                await s.WriteAsync(MllpFramer.Frame(ack));
                await s.FlushAsync();
                break;
            }
        });

        var options = new TcpOutboundOptions { Host = "127.0.0.1", Port = port, ExpectReply = true };
        await using var endpoint = new TcpOutboundEndpoint(options, codec: null);   // codec optional -> raw pass-through
        var result = await endpoint.SendAsync(MessageContextBuilder.Create(payload: "REQ"), CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
        Assert.Equal(ack, result.ResponsePayload.ToArray());
        await server;
        Assert.NotNull(received);
        Assert.Equal("REQ", Encoding.UTF8.GetString(received!));
        listener.Stop();
    }

    [Fact]
    public async Task Returns_failed_when_destination_unreachable()
    {
        var options = new TcpOutboundOptions { Host = "127.0.0.1", Port = TestSupport.GetFreePort(), ExpectReply = true };
        await using var endpoint = new TcpOutboundEndpoint(options, codec: null);

        var result = await endpoint.SendAsync(MessageContextBuilder.Create(payload: "x"), CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task Reuses_pooled_connection_across_sends()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        int accepts = 0;
        using var serverCts = new CancellationTokenSource();

        var server = Task.Run(async () =>
        {
            var c = await listener.AcceptTcpClientAsync(serverCts.Token);
            Interlocked.Increment(ref accepts);
            var s = c.GetStream();
            await foreach (var _ in MllpFramer.ReadMessagesAsync(s, serverCts.Token))
            {
                await s.WriteAsync(MllpFramer.Frame(Encoding.UTF8.GetBytes("MSA|AA")), serverCts.Token);
                await s.FlushAsync(serverCts.Token);
            }
        });

        var options = new TcpOutboundOptions { Host = "127.0.0.1", Port = port, ExpectReply = true, PoolSize = 2 };
        await using var endpoint = new TcpOutboundEndpoint(options, codec: null);

        var r1 = await endpoint.SendAsync(MessageContextBuilder.Create(payload: "a"), CancellationToken.None);
        var r2 = await endpoint.SendAsync(MessageContextBuilder.Create(payload: "b"), CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Delivered, r1.Outcome);
        Assert.Equal(DeliveryOutcome.Delivered, r2.Outcome);
        Assert.Equal(1, Volatile.Read(ref accepts));            // same connection reused
        serverCts.Cancel();
        listener.Stop();
    }

    [Fact]
    public async Task Reconnects_and_delivers_after_the_pooled_connection_is_closed_by_the_peer()
    {
        // Simulates a downstream that closes the connection while idle (the demo receiver's 2s read
        // timeout, a firewall/NAT reap, etc.): each accepted connection handles exactly one message
        // then closes. The pooled connection is therefore dead by the next send, and the endpoint must
        // transparently reconnect instead of dropping the message (the Option 1 fix).
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        int accepts = 0;
        var firstClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var serverCts = new CancellationTokenSource();

        var server = Task.Run(async () =>
        {
            while (!serverCts.IsCancellationRequested)
            {
                TcpClient c;
                try { c = await listener.AcceptTcpClientAsync(serverCts.Token); }
                catch (OperationCanceledException) { break; }

                int n = Interlocked.Increment(ref accepts);
                using (c)
                {
                    c.NoDelay = true;
                    var s = c.GetStream();
                    await foreach (var _ in MllpFramer.ReadMessagesAsync(s, serverCts.Token))
                    {
                        await s.WriteAsync(MllpFramer.Frame(Encoding.UTF8.GetBytes("MSA|AA")), serverCts.Token);
                        await s.FlushAsync(serverCts.Token);
                        break;                                  // one message per connection, then close
                    }
                }                                               // dispose => close the connection (peer-close)
                if (n == 1) firstClosed.TrySetResult();
            }
        });

        var options = new TcpOutboundOptions { Host = "127.0.0.1", Port = port, ExpectReply = true, PoolSize = 1 };
        await using var endpoint = new TcpOutboundEndpoint(options, codec: null);

        var r1 = await endpoint.SendAsync(MessageContextBuilder.Create(payload: "a"), CancellationToken.None);
        Assert.Equal(DeliveryOutcome.Delivered, r1.Outcome);

        await firstClosed.Task.WaitAsync(TimeSpan.FromSeconds(5));   // the pooled connection is now dead before the next send

        var r2 = await endpoint.SendAsync(MessageContextBuilder.Create(payload: "b"), CancellationToken.None);
        Assert.Equal(DeliveryOutcome.Delivered, r2.Outcome);        // transparently reconnected, not dropped

        Assert.Equal(2, Volatile.Read(ref accepts));                // a fresh connection was established for the retry
        serverCts.Cancel();
        listener.Stop();
    }

    [Fact]
    public async Task DuplexOutbound_sends_receives_ack_and_dispatches_unsolicited_frames()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var unsolicited = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            correlationId = "tcp-correlation-1",
            requestId = "tcp-request-1",
            messageId = "tcp-message-1",
            logicalEndpointId = "remote-tcp",
            payload = "MSH|UNSOLICITED"
        }));
        var ack = Encoding.UTF8.GetBytes("MSA|AA");
        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var server = Task.Run(async () =>
        {
            using var c = await listener.AcceptTcpClientAsync(serverCts.Token);
            c.NoDelay = true;
            var s = c.GetStream();

            await s.WriteAsync(MllpFramer.Frame(unsolicited), serverCts.Token);
            await s.FlushAsync(serverCts.Token);

            await foreach (var _ in MllpFramer.ReadMessagesAsync(s, serverCts.Token))
            {
                await s.WriteAsync(MllpFramer.Frame(ack), serverCts.Token);
                await s.FlushAsync(serverCts.Token);
                break;
            }
        }, serverCts.Token);

        var dispatcher = new FakeMessageDispatcher();
        var options = new TcpOutboundOptions
        {
            Mode = CommunicationMode.DuplexOutbound,
            SourceEndpointId = 77,
            Host = "127.0.0.1",
            Port = port,
            ExpectReply = true,
            ReplyCorrelationTimeout = TimeSpan.FromSeconds(5),
            ReconnectDelay = TimeSpan.FromMilliseconds(50),
        };
        await using var endpoint = new TcpOutboundEndpoint(options, codec: null);
        endpoint.ConfigureInboundDispatch(dispatcher, new FakeReplyContextFactory());
        await endpoint.StartAsync(CancellationToken.None);

        await TestSupport.WaitForAsync(() => dispatcher.Dispatched.Count == 1, TimeSpan.FromSeconds(5));
        Assert.Equal(77, dispatcher.Dispatched[0].SourceEndpointId);
        Assert.Equal("tcp-correlation-1", dispatcher.Dispatched[0].CorrelationId);
        Assert.Equal("MSH|UNSOLICITED", Encoding.UTF8.GetString(dispatcher.Dispatched[0].Payload.ToArray()));
        Assert.Equal("tcp-request-1", dispatcher.Dispatched[0].Headers[TransportCorrelationHeaders.RequestId]);
        Assert.Equal("tcp-message-1", dispatcher.Dispatched[0].Headers[TransportCorrelationHeaders.MessageId]);
        Assert.Equal("remote-tcp", dispatcher.Dispatched[0].Headers[TransportCorrelationHeaders.LogicalEndpointId]);

        var result = await endpoint.SendAsync(MessageContextBuilder.Create(payload: "REQ"), CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
        Assert.Equal(ack, result.ResponsePayload.ToArray());

        await endpoint.StopAsync(CancellationToken.None);
        listener.Stop();
        await server;
    }

    [Fact]
    public async Task DuplexInbound_sends_over_accepted_inbound_session_and_receives_ack()
    {
        var registry = new TcpDuplexSessionRegistry();
        var inboundOptions = new TcpInboundOptions
        {
            Mode = CommunicationMode.DuplexInbound,
            SourceEndpointId = 88,
            Port = 0,
        };
        await using var inbound = new TcpInboundEndpoint(inboundOptions, new FakeMessageDispatcher(), new FakeReplyContextFactory(), duplexSessions: registry);
        await inbound.StartAsync(CancellationToken.None);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, inbound.BoundPort);
        client.NoDelay = true;
        var stream = client.GetStream();

        var ack = Encoding.UTF8.GetBytes("MSA|AA");
        byte[]? received = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var partner = Task.Run(async () =>
        {
            await foreach (var msg in MllpFramer.ReadMessagesAsync(stream, cts.Token))
            {
                received = msg;
                await stream.WriteAsync(MllpFramer.Frame(ack), cts.Token);
                await stream.FlushAsync(cts.Token);
                break;
            }
        }, cts.Token);

        var outboundOptions = new TcpOutboundOptions
        {
            Mode = CommunicationMode.DuplexInbound,
            Host = "127.0.0.1",
            Port = inbound.BoundPort,
            DuplexInboundSourceEndpointId = 88,
            ExpectReply = true,
            ReplyCorrelationTimeout = TimeSpan.FromSeconds(5),
        };
        await using var outbound = new TcpOutboundEndpoint(outboundOptions, codec: null, duplexSessions: registry);

        DeliveryResult result;
        var sendDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        do
        {
            result = await outbound.SendAsync(MessageContextBuilder.Create(payload: "REQ"), CancellationToken.None);
            if (result.Outcome == DeliveryOutcome.Delivered)
                break;
            await Task.Delay(20, cts.Token);
        } while (DateTimeOffset.UtcNow < sendDeadline);

        Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
        Assert.Equal(ack, result.ResponsePayload.ToArray());
        Assert.Equal("REQ", Encoding.UTF8.GetString(received!));

        await partner;
        client.Dispose();

        DeliveryResult afterDisconnect;
        var disconnectDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        do
        {
            afterDisconnect = await outbound.SendAsync(MessageContextBuilder.Create(payload: "AFTER"), CancellationToken.None);
            if (afterDisconnect.Outcome == DeliveryOutcome.Failed)
                break;
            await Task.Delay(20, cts.Token);
        } while (DateTimeOffset.UtcNow < disconnectDeadline);

        Assert.Equal(DeliveryOutcome.Failed, afterDisconnect.Outcome);
        await inbound.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Inbound_preserves_envelope_correlation_metadata()
    {
        var dispatcher = new FakeMessageDispatcher();
        var options = new TcpInboundOptions
        {
            SourceEndpointId = 99,
            Port = 0,
        };
        await using var inbound = new TcpInboundEndpoint(options, dispatcher, new FakeReplyContextFactory());
        await inbound.StartAsync(CancellationToken.None);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, inbound.BoundPort);
        var stream = client.GetStream();
        var envelope = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            correlationId = "tcp-correlation-2",
            requestId = "tcp-request-2",
            messageId = "tcp-message-2",
            logicalEndpointId = "client-tcp",
            payload = "MSH|INBOUND"
        }));

        await stream.WriteAsync(MllpFramer.Frame(envelope));
        await stream.FlushAsync();

        await TestSupport.WaitForAsync(() => dispatcher.Dispatched.Count == 1, TimeSpan.FromSeconds(5));
        Assert.Equal("tcp-correlation-2", dispatcher.Dispatched[0].CorrelationId);
        Assert.Equal("MSH|INBOUND", Encoding.UTF8.GetString(dispatcher.Dispatched[0].Payload.ToArray()));
        Assert.Equal("tcp-request-2", dispatcher.Dispatched[0].Headers[TransportCorrelationHeaders.RequestId]);
        Assert.Equal("tcp-message-2", dispatcher.Dispatched[0].Headers[TransportCorrelationHeaders.MessageId]);
        Assert.Equal("client-tcp", dispatcher.Dispatched[0].Headers[TransportCorrelationHeaders.LogicalEndpointId]);

        await inbound.StopAsync(CancellationToken.None);
    }
}
