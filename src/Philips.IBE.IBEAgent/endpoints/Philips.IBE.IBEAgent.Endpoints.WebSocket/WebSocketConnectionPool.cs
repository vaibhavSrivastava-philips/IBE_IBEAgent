using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using Philips.IBE.IBEAgent.Security;
namespace Philips.IBE.IBEAgent.Endpoints.WebSocket;

internal sealed class WebSocketConnectionPool(Uri endpoint, int size, SslOptions? ssl = null, ProxyOptions? proxy = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _slots = new(size, size);
    private readonly ConcurrentQueue<ClientWebSocket> _idle = new();
    private readonly SslOptions _ssl = ssl ?? new SslOptions();
    private readonly ProxyOptions _proxy = proxy ?? new ProxyOptions();

    public async Task<ClientWebSocket> RentAsync(bool forceFresh, CancellationToken ct)
    {
        await _slots.WaitAsync(ct);
        ClientWebSocket? existing = null;
        if (!forceFresh && _idle.TryDequeue(out existing) && existing.State == WebSocketState.Open) return existing;
        existing?.Dispose();

        var socket = new ClientWebSocket();

        if (_ssl.IsEnabled)
        {
            socket.Options.RemoteCertificateValidationCallback = _ssl.CreateRemoteCertificateValidator();
            if (_ssl.RequiresRemoteCertificate)
            {
                var clientCertificate = _ssl.LoadLocalCertificate()
                    ?? throw new InvalidOperationException(
                        $"WebSocket outbound endpoint ({endpoint}) has SSL mode Mutual but no CertificatePath configured.");
                socket.Options.ClientCertificates.Add(clientCertificate);
            }
        }

        if (_proxy.IsEnabled)
        {
            var proxyUri = new Uri($"http://{_proxy.Host}:{_proxy.Port}");
            var webProxy = new WebProxy(proxyUri);
            if (_proxy.HasCredentials)
                webProxy.Credentials = new NetworkCredential(_proxy.Username, _proxy.Password);
            socket.Options.Proxy = webProxy;
        }

        await socket.ConnectAsync(endpoint, ct);
        return socket;
    }

    public void Return(ClientWebSocket socket)                          // healthy -> reuse
    {
        if (socket.State == WebSocketState.Open) _idle.Enqueue(socket); else socket.Dispose();
        _slots.Release();
    }

    public void Discard(ClientWebSocket socket)                         // broken -> drop
    {
        socket.Dispose();
        _slots.Release();
    }

    public ValueTask DisposeAsync()
    {
        while (_idle.TryDequeue(out var s)) s.Dispose();
        _slots.Dispose();
        return ValueTask.CompletedTask;
    }
}
