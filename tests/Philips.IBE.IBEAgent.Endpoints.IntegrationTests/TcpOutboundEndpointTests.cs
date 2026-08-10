using System.Net;
using System.Net.Sockets;
using System.Text;
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
}