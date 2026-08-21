using System.Net;
using System.Net.Sockets;
using System.Text;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Endpoints.Http;
using Philips.IBE.IBEAgent.Security;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class HttpProxyEndpointTests
{
    [Fact]
    public async Task Outbound_sends_request_through_anonymous_forward_proxy()
    {
        await using var proxy = new FakeHttpForwardProxy();
        int proxyPort = await proxy.StartAsync();

        var destinationPrefix = $"http://localhost:{TestSupport.GetFreePort()}/ibe/";
        var dispatcher = new FakeMessageDispatcher();
        var destinationOptions = new HttpInboundOptions
        {
            SourceEndpointId = 5, Prefix = destinationPrefix, ReplyTimeoutInMs = 10_000,
        };
        await using var destination = new HttpInboundEndpoint(destinationOptions, dispatcher, new FakeReplyContextFactory());
        await destination.StartAsync(CancellationToken.None);

        var options = new HttpOutboundOptions
        {
            Endpoint = new Uri(destinationPrefix),
            Proxy = new ProxyOptions { IsEnabled = true, Host = "127.0.0.1", Port = proxyPort },
        };
        using var endpoint = new HttpOutboundEndpoint(options, codec: null);

        var sendTask = endpoint.SendAsync(MessageContextBuilder.Create(payload: "VIA-HTTP-PROXY"), CancellationToken.None);

        await TestSupport.WaitForAsync(() => dispatcher.Dispatched.Count == 1, TimeSpan.FromSeconds(5));
        await dispatcher.Dispatched[0].Ack.WriteAsync(Encoding.UTF8.GetBytes("OK"), CancellationToken.None);

        var result = await sendTask;
        Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
        Assert.True(proxy.RequestCount > 0);
    }

    [Fact]
    public async Task Outbound_sends_request_through_authenticated_forward_proxy()
    {
        await using var proxy = new FakeHttpForwardProxy(requireCredentials: ("agent", "p@ss"));
        int proxyPort = await proxy.StartAsync();

        var destinationPrefix = $"http://localhost:{TestSupport.GetFreePort()}/ibe/";
        var dispatcher = new FakeMessageDispatcher();
        var destinationOptions = new HttpInboundOptions
        {
            SourceEndpointId = 6, Prefix = destinationPrefix, ReplyTimeoutInMs = 10_000,
        };
        await using var destination = new HttpInboundEndpoint(destinationOptions, dispatcher, new FakeReplyContextFactory());
        await destination.StartAsync(CancellationToken.None);

        var options = new HttpOutboundOptions
        {
            Endpoint = new Uri(destinationPrefix),
            Proxy = new ProxyOptions { IsEnabled = true, Host = "127.0.0.1", Port = proxyPort, Username = "agent", Password = "p@ss" },
        };
        using var endpoint = new HttpOutboundEndpoint(options, codec: null);

        var sendTask = endpoint.SendAsync(MessageContextBuilder.Create(payload: "AUTH-PROXY"), CancellationToken.None);

        await TestSupport.WaitForAsync(() => dispatcher.Dispatched.Count == 1, TimeSpan.FromSeconds(5));
        await dispatcher.Dispatched[0].Ack.WriteAsync(Encoding.UTF8.GetBytes("OK"), CancellationToken.None);

        var result = await sendTask;
        Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
    }
}

// Minimal HTTP forward-proxy test double for a *plain* http:// destination: relays the raw request
// line/headers/body to the destination host (parsed from the absolute-form request URI) and streams
// the response back. Optionally requires HTTP Basic Proxy-Authorization.
internal sealed class FakeHttpForwardProxy((string User, string Password)? requireCredentials = null) : IAsyncDisposable
{
    private TcpListener? _listener;
    private Task? _acceptLoop;
    private readonly CancellationTokenSource _cts = new();
    public int RequestCount { get; private set; }

    public async Task<int> StartAsync()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_cts.Token);
        return ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                _ = HandleAsync(client, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            var stream = client.GetStream();
            var (headerText, bodyPrefix) = await ReadHeadersAsync(stream, ct);
            var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            var requestLine = lines[0]; // "POST http://host:port/path HTTP/1.1"

            if (requireCredentials is { } creds)
            {
                var expected = "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes($"{creds.User}:{creds.Password}"));
                var authHeader = lines.FirstOrDefault(l => l.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase));
                if (authHeader is null || !authHeader["Proxy-Authorization:".Length..].Trim().Equals(expected, StringComparison.Ordinal))
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(
                        "HTTP/1.1 407 Proxy Authentication Required\r\nProxy-Authenticate: Basic realm=\"proxy\"\r\nContent-Length: 0\r\n\r\n"), ct);
                    return;
                }
            }

            RequestCount++;

            var requestUri = new Uri(requestLine.Split(' ')[1]);
            int contentLength = 0;
            foreach (var l in lines)
                if (l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    contentLength = int.Parse(l["Content-Length:".Length..].Trim());

            var body = new List<byte>(bodyPrefix);
            while (body.Count < contentLength)
            {
                var buf = new byte[8192];
                int n = await stream.ReadAsync(buf, ct);
                if (n == 0) break;
                body.AddRange(buf.AsSpan(0, n).ToArray());
            }

            using var destClient = new TcpClient();
            await destClient.ConnectAsync(requestUri.Host, requestUri.Port, ct);
            var destStream = destClient.GetStream();

            var forwardedHeaders = new StringBuilder()
                .Append(requestLine.Replace(requestUri.GetLeftPart(UriPartial.Authority), "", StringComparison.Ordinal)).Append("\r\n");
            foreach (var l in lines.Skip(1))
                if (!l.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase))
                    forwardedHeaders.Append(l).Append("\r\n");
            forwardedHeaders.Append("\r\n");

            await destStream.WriteAsync(Encoding.ASCII.GetBytes(forwardedHeaders.ToString()), ct);
            await destStream.WriteAsync(body.ToArray(), ct);
            await destStream.FlushAsync(ct);

            await destStream.CopyToAsync(stream, ct);
        }
    }

    private static async Task<(string Headers, byte[] BodyPrefix)> ReadHeadersAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var acc = new List<byte>();
        while (true)
        {
            int n = await stream.ReadAsync(buffer, ct);
            if (n == 0) throw new IOException("client closed before headers completed");
            acc.AddRange(buffer.AsSpan(0, n).ToArray());
            var text = Encoding.ASCII.GetString(acc.ToArray());
            var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end >= 0)
                return (text[..end], acc.Skip(end + 4).ToArray());
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener?.Stop();
        if (_acceptLoop is not null)
            try { await _acceptLoop; } catch { }
        _cts.Dispose();
    }
}
