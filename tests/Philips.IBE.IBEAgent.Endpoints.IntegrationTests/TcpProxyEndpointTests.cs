using System.Net;
using System.Net.Sockets;
using System.Text;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Endpoints.Tcp;
using Philips.IBE.IBEAgent.Security;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class TcpProxyEndpointTests
{
    [Fact]
    public async Task Outbound_connects_through_anonymous_forward_proxy()
    {
        // Real destination server (what the proxy is expected to CONNECT us through to).
        var destinationListener = new TcpListener(IPAddress.Loopback, 0);
        destinationListener.Start();
        int destinationPort = ((IPEndPoint)destinationListener.LocalEndpoint).Port;
        byte[]? received = null;

        var destinationTask = Task.Run(async () =>
        {
            using var c = await destinationListener.AcceptTcpClientAsync();
            var s = c.GetStream();
            await foreach (var msg in MllpFramer.ReadMessagesAsync(s, CancellationToken.None))
            {
                received = msg;
                await s.WriteAsync(MllpFramer.Frame(Encoding.UTF8.GetBytes("MSA|AA")));
                await s.FlushAsync();
                break;
            }
        });

        await using var proxy = new FakeConnectProxy();
        int proxyPort = await proxy.StartAsync();

        var options = new TcpOutboundOptions
        {
            Host = "127.0.0.1",
            Port = destinationPort,
            ExpectReply = true,
            Proxy = new ProxyOptions { IsEnabled = true, Host = "127.0.0.1", Port = proxyPort },
        };
        await using var endpoint = new TcpOutboundEndpoint(options, codec: null);

        var result = await endpoint.SendAsync(MessageContextBuilder.Create(payload: "VIA-PROXY"), CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
        await destinationTask;
        Assert.Equal("VIA-PROXY", Encoding.UTF8.GetString(received!));
        Assert.True(proxy.SawConnectRequest);
        destinationListener.Stop();
    }

    [Fact]
    public async Task Outbound_connects_through_authenticated_forward_proxy()
    {
        var destinationListener = new TcpListener(IPAddress.Loopback, 0);
        destinationListener.Start();
        int destinationPort = ((IPEndPoint)destinationListener.LocalEndpoint).Port;

        var destinationTask = Task.Run(async () =>
        {
            using var c = await destinationListener.AcceptTcpClientAsync();
            var s = c.GetStream();
            await foreach (var _ in MllpFramer.ReadMessagesAsync(s, CancellationToken.None))
            {
                await s.WriteAsync(MllpFramer.Frame(Encoding.UTF8.GetBytes("MSA|AA")));
                await s.FlushAsync();
                break;
            }
        });

        await using var proxy = new FakeConnectProxy(requireCredentials: ("user1", "s3cret"));
        int proxyPort = await proxy.StartAsync();

        var options = new TcpOutboundOptions
        {
            Host = "127.0.0.1",
            Port = destinationPort,
            ExpectReply = true,
            Proxy = new ProxyOptions
            {
                IsEnabled = true, Host = "127.0.0.1", Port = proxyPort,
                Username = "user1", Password = "s3cret",
            },
        };
        await using var endpoint = new TcpOutboundEndpoint(options, codec: null);

        var result = await endpoint.SendAsync(MessageContextBuilder.Create(payload: "AUTH"), CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
        await destinationTask;
        destinationListener.Stop();
    }

    [Fact]
    public async Task Outbound_fails_when_proxy_rejects_bad_credentials()
    {
        await using var proxy = new FakeConnectProxy(requireCredentials: ("user1", "s3cret"));
        int proxyPort = await proxy.StartAsync();

        var options = new TcpOutboundOptions
        {
            Host = "127.0.0.1",
            Port = TestSupport.GetFreePort(),
            ExpectReply = true,
            Proxy = new ProxyOptions
            {
                IsEnabled = true, Host = "127.0.0.1", Port = proxyPort,
                Username = "user1", Password = "wrong-password",
            },
        };
        await using var endpoint = new TcpOutboundEndpoint(options, codec: null);

        var result = await endpoint.SendAsync(MessageContextBuilder.Create(payload: "x"), CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Failed, result.Outcome);
    }
}

// Minimal forward-proxy test double: accepts one CONNECT per connection, optionally requiring Basic
// Proxy-Authorization, then splices bytes between the client and the real destination.
internal sealed class FakeConnectProxy((string User, string Password)? requireCredentials = null) : IAsyncDisposable
{
    private TcpListener? _listener;
    private Task? _acceptLoop;
    private readonly CancellationTokenSource _cts = new();
    public bool SawConnectRequest { get; private set; }

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
            var (requestLine, headers) = await ReadHttpHeadersAsync(stream, ct);
            SawConnectRequest = requestLine.StartsWith("CONNECT ", StringComparison.OrdinalIgnoreCase);

            if (requireCredentials is { } creds)
            {
                var expected = "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes($"{creds.User}:{creds.Password}"));
                var authHeader = headers.FirstOrDefault(h => h.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase));
                if (authHeader is null || !authHeader["Proxy-Authorization:".Length..].Trim().Equals(expected, StringComparison.Ordinal))
                {
                    var deny = Encoding.ASCII.GetBytes("HTTP/1.1 407 Proxy Authentication Required\r\n\r\n");
                    await stream.WriteAsync(deny, ct);
                    return;
                }
            }

            // requestLine: "CONNECT host:port HTTP/1.1"
            var target = requestLine.Split(' ')[1];
            var parts = target.Split(':');
            var destHost = parts[0];
            var destPort = int.Parse(parts[1]);

            using var destClient = new TcpClient();
            try { await destClient.ConnectAsync(destHost, destPort, ct); }
            catch
            {
                var fail = Encoding.ASCII.GetBytes("HTTP/1.1 502 Bad Gateway\r\n\r\n");
                await stream.WriteAsync(fail, ct);
                return;
            }

            var ok = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
            await stream.WriteAsync(ok, ct);
            await stream.FlushAsync(ct);

            var destStream = destClient.GetStream();
            var t1 = stream.CopyToAsync(destStream, ct);
            var t2 = destStream.CopyToAsync(stream, ct);
            try { await Task.WhenAny(t1, t2); } catch { /* connection closed either direction */ }
        }
    }

    private static async Task<(string RequestLine, List<string> Headers)> ReadHttpHeadersAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var acc = new List<byte>();
        while (true)
        {
            int n = await stream.ReadAsync(buffer, ct);
            if (n == 0) throw new IOException("client closed before sending CONNECT headers");
            acc.AddRange(buffer.AsSpan(0, n).ToArray());
            var text = Encoding.ASCII.GetString(acc.ToArray());
            var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end < 0) continue;

            var lines = text[..end].Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();
            return (lines[0], lines.Skip(1).ToList());
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
