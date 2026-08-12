using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Endpoints.WebSocket;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class WebSocketDuplexEndpointTests
{
    [Fact]
    public async Task DuplexOutbound_sends_receives_reply_and_dispatches_unsolicited_messages()
    {
        var prefix = $"http://localhost:{TestSupport.GetFreePort()}/ws/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();
        var unsolicited = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            correlationId = "ws-correlation-1",
            requestId = "ws-request-1",
            messageId = "ws-message-1",
            logicalEndpointId = "remote-ws",
            payload = "MSH|UNSOLICITED"
        }));
        var ack = Encoding.UTF8.GetBytes("MSA|AA");
        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var server = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync().WaitAsync(serverCts.Token);
            var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
            using var socket = wsContext.WebSocket;

            await socket.SendAsync(unsolicited, WebSocketMessageType.Text, endOfMessage: true, serverCts.Token);

            var request = await ReceiveOneAsync(socket, serverCts.Token);
            Assert.Equal("REQ", Encoding.UTF8.GetString(request));
            await socket.SendAsync(ack, WebSocketMessageType.Binary, endOfMessage: true, serverCts.Token);
        }, serverCts.Token);

        var dispatcher = new FakeMessageDispatcher();
        await using var endpoint = new WebSocketOutboundEndpoint(
            new WebSocketOutboundOptions
            {
                Mode = CommunicationMode.DuplexOutbound,
                SourceEndpointId = 77,
                Endpoint = new Uri(prefix.Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase)),
                ExpectReply = true,
                ReplyCorrelationTimeout = TimeSpan.FromSeconds(5),
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
            },
            codec: null);
        endpoint.ConfigureInboundDispatch(dispatcher, new FakeReplyContextFactory());
        await endpoint.StartAsync(CancellationToken.None);

        await TestSupport.WaitForAsync(() => dispatcher.Dispatched.Count == 1, TimeSpan.FromSeconds(5));
        Assert.Equal(77, dispatcher.Dispatched[0].SourceEndpointId);
        Assert.Equal("ws-correlation-1", dispatcher.Dispatched[0].CorrelationId);
        Assert.Equal("MSH|UNSOLICITED", Encoding.UTF8.GetString(dispatcher.Dispatched[0].Payload.ToArray()));
        Assert.Equal("ws-request-1", dispatcher.Dispatched[0].Headers[TransportCorrelationHeaders.RequestId]);
        Assert.Equal("ws-message-1", dispatcher.Dispatched[0].Headers[TransportCorrelationHeaders.MessageId]);
        Assert.Equal("remote-ws", dispatcher.Dispatched[0].Headers[TransportCorrelationHeaders.LogicalEndpointId]);

        var result = await endpoint.SendAsync(MessageContextBuilder.Create(payload: "REQ"), CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
        Assert.Equal(ack, result.ResponsePayload.ToArray());

        await endpoint.StopAsync(CancellationToken.None);
        listener.Stop();
        await server;
    }

    [Fact]
    public async Task DuplexInbound_sends_over_accepted_inbound_session_and_receives_reply()
    {
        var registry = new WebSocketDuplexSessionRegistry();
        var prefix = $"http://localhost:{TestSupport.GetFreePort()}/ws/";
        await using var inbound = new WebSocketInboundEndpoint(
            new WebSocketInboundOptions
            {
                Mode = CommunicationMode.DuplexInbound,
                SourceEndpointId = 88,
                Prefix = prefix,
            },
            new FakeMessageDispatcher(),
            new FakeReplyContextFactory(),
            registry);
        await inbound.StartAsync(CancellationToken.None);

        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri(prefix.Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase)), CancellationToken.None);

        var ack = Encoding.UTF8.GetBytes("MSA|AA");
        byte[]? received = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var partner = Task.Run(async () =>
        {
            received = await ReceiveOneAsync(client, cts.Token);
            await client.SendAsync(ack, WebSocketMessageType.Binary, endOfMessage: true, cts.Token);
        }, cts.Token);

        await using var outbound = new WebSocketOutboundEndpoint(
            new WebSocketOutboundOptions
            {
                Mode = CommunicationMode.DuplexInbound,
                DuplexInboundSourceEndpointId = 88,
                Endpoint = new Uri("ws://localhost/unused"),
                ExpectReply = true,
                ReplyCorrelationTimeout = TimeSpan.FromSeconds(5),
            },
            codec: null,
            registry);

        DeliveryResult result;
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        do
        {
            result = await outbound.SendAsync(MessageContextBuilder.Create(payload: "REQ"), CancellationToken.None);
            if (result.Outcome == DeliveryOutcome.Delivered)
                break;
            await Task.Delay(20, cts.Token);
        } while (DateTimeOffset.UtcNow < deadline);

        Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
        Assert.Equal(ack, result.ResponsePayload.ToArray());
        Assert.Equal("REQ", Encoding.UTF8.GetString(received!));

        await partner;
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        await inbound.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Inbound_preserves_text_envelope_correlation_metadata()
    {
        var prefix = $"http://localhost:{TestSupport.GetFreePort()}/ws/";
        var dispatcher = new FakeMessageDispatcher();
        await using var inbound = new WebSocketInboundEndpoint(
            new WebSocketInboundOptions
            {
                SourceEndpointId = 99,
                Prefix = prefix,
            },
            dispatcher,
            new FakeReplyContextFactory());
        await inbound.StartAsync(CancellationToken.None);

        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri(prefix.Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase)), CancellationToken.None);
        var envelope = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            correlationId = "ws-correlation-2",
            requestId = "ws-request-2",
            messageId = "ws-message-2",
            logicalEndpointId = "client-ws",
            payload = "MSH|INBOUND"
        }));

        await client.SendAsync(envelope, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

        await TestSupport.WaitForAsync(() => dispatcher.Dispatched.Count == 1, TimeSpan.FromSeconds(5));
        Assert.Equal("ws-correlation-2", dispatcher.Dispatched[0].CorrelationId);
        Assert.Equal("MSH|INBOUND", Encoding.UTF8.GetString(dispatcher.Dispatched[0].Payload.ToArray()));
        Assert.Equal("ws-request-2", dispatcher.Dispatched[0].Headers[TransportCorrelationHeaders.RequestId]);
        Assert.Equal("ws-message-2", dispatcher.Dispatched[0].Headers[TransportCorrelationHeaders.MessageId]);
        Assert.Equal("client-ws", dispatcher.Dispatched[0].Headers[TransportCorrelationHeaders.LogicalEndpointId]);

        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        await inbound.StopAsync(CancellationToken.None);
    }

    private static async Task<byte[]> ReceiveOneAsync(System.Net.WebSockets.WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var acc = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return [];
            acc.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return acc.ToArray();
    }
}
