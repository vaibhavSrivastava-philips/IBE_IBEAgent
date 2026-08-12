using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Endpoints.WebSocket;
using Philips.IBE.IBEAgent.Security;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class WebSocketProxyEndpointTests
{
    [Fact]
    public async Task Outbound_connects_through_anonymous_forward_proxy()
    {
        var destinationPrefix = $"http://localhost:{TestSupport.GetFreePort()}/ws/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(destinationPrefix);
        listener.Start();
        byte[]? received = null;
        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var server = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync().WaitAsync(serverCts.Token);
            var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
            using var socket = wsContext.WebSocket;
            var buffer = new byte[8192];
            var result = await socket.ReceiveAsync(buffer, serverCts.Token);
            received = buffer[..result.Count];
            await socket.SendAsync("MSA|AA"u8.ToArray(), WebSocketMessageType.Binary, endOfMessage: true, serverCts.Token);
        }, serverCts.Token);

        await using var proxy = new FakeConnectProxy();
        int proxyPort = await proxy.StartAsync();

        var options = new WebSocketOutboundOptions
        {
            Endpoint = new Uri(destinationPrefix.Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase)),
            Proxy = new ProxyOptions { IsEnabled = true, Host = "127.0.0.1", Port = proxyPort },
        };
        await using var endpoint = new WebSocketOutboundEndpoint(options, codec: null);

        var result = await endpoint.SendAsync(MessageContextBuilder.Create(payload: "VIA-WS-PROXY"), CancellationToken.None);

        await server;
        listener.Stop();

        Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
        Assert.Equal("VIA-WS-PROXY", Encoding.UTF8.GetString(received!));
        Assert.Equal("MSA|AA", Encoding.UTF8.GetString(result.ResponsePayload.ToArray()));
        Assert.True(proxy.SawConnectRequest);
    }

    [Fact]
    public async Task Outbound_fails_when_proxy_rejects_bad_credentials()
    {
        await using var proxy = new FakeConnectProxy(requireCredentials: ("agent", "p@ss"));
        int proxyPort = await proxy.StartAsync();

        var options = new WebSocketOutboundOptions
        {
            Endpoint = new Uri($"ws://localhost:{TestSupport.GetFreePort()}/ws/"),
            Proxy = new ProxyOptions { IsEnabled = true, Host = "127.0.0.1", Port = proxyPort, Username = "agent", Password = "wrong" },
        };
        await using var endpoint = new WebSocketOutboundEndpoint(options, codec: null);

        var result = await endpoint.SendAsync(MessageContextBuilder.Create(payload: "x"), CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Failed, result.Outcome);
    }
}
