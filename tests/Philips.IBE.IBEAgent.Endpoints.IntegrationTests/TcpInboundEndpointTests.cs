using System.Net;
using System.Net.Sockets;
using System.Text;
using Philips.IBE.IBEAgent.Endpoints.Tcp;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class TcpInboundEndpointTests
{
    [Fact]
    public async Task Receives_message_then_writes_ack_back_on_same_connection()
    {
        var dispatcher = new FakeMessageDispatcher();
        var options = new TcpInboundOptions { SourceEndpointId = 7, Port = 0, Format = "hl7v2" };
        await using var endpoint = new TcpInboundEndpoint(options, dispatcher, new FakeReplyContextFactory());
        await endpoint.StartAsync(CancellationToken.None);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, endpoint.BoundPort);
        var stream = client.GetStream();

        var hl7 = Encoding.UTF8.GetBytes("MSH|^~\\&|SRC|FAC|DST");
        await stream.WriteAsync(MllpFramer.Frame(hl7));
        await stream.FlushAsync();

        await TestSupport.WaitForAsync(() => dispatcher.Dispatched.Count == 1, TimeSpan.FromSeconds(5));
        var ctx = dispatcher.Dispatched[0];
        Assert.Equal(7, ctx.SourceEndpointId);
        Assert.Equal("hl7v2", ctx.Format);
        Assert.Equal(hl7, ctx.Payload.ToArray());

        // Drive the reply the way the real ReplyContext will (Phase 3):
        var ack = Encoding.UTF8.GetBytes("MSH|...\rMSA|AA|1");
        await ctx.Ack.WriteAsync(ack, CancellationToken.None);

        var replyFrame = await TestSupport.ReadOneFrameAsync(stream, TimeSpan.FromSeconds(5));
        Assert.Equal(ack, replyFrame);
    }

    [Fact]
    public async Task Receives_multiple_messages_on_one_connection()
    {
        var dispatcher = new FakeMessageDispatcher();
        var options = new TcpInboundOptions { SourceEndpointId = 1, Port = 0 };
        await using var endpoint = new TcpInboundEndpoint(options, dispatcher, new FakeReplyContextFactory());
        await endpoint.StartAsync(CancellationToken.None);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, endpoint.BoundPort);
        var stream = client.GetStream();

        await stream.WriteAsync(MllpFramer.Frame(Encoding.UTF8.GetBytes("ONE")));
        await stream.WriteAsync(MllpFramer.Frame(Encoding.UTF8.GetBytes("TWO")));
        await stream.FlushAsync();

        await TestSupport.WaitForAsync(() => dispatcher.Dispatched.Count == 2, TimeSpan.FromSeconds(5));
        Assert.Equal("ONE", Encoding.UTF8.GetString(dispatcher.Dispatched[0].Payload.ToArray()));
        Assert.Equal("TWO", Encoding.UTF8.GetString(dispatcher.Dispatched[1].Payload.ToArray()));
    }
}