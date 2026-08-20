using System.Net;
using System.Net.WebSockets;
using Philips.IBE.IBEAgent.Security;

namespace Philips.IBE.IBEAgent.Endpoints.WebSocket;

// SRP: WebSocketConnectionPool owns slot management / idle queue only.
// Creating the physical WebSocket (SSL config, proxy config, connect) is the sole responsibility of this interface.
internal interface IWebSocketConnectionFactory
{
    Task<ClientWebSocket> CreateAsync(CancellationToken ct);
}

// Default production factory: configures SSL and proxy on a new ClientWebSocket then connects it.
internal sealed class WebSocketConnectionFactory(Uri endpoint, SslOptions ssl, ProxyOptions proxy)
    : IWebSocketConnectionFactory
{
    public async Task<ClientWebSocket> CreateAsync(CancellationToken ct)
    {
        var socket = new ClientWebSocket();

        if (ssl.IsEnabled)
        {
            socket.Options.RemoteCertificateValidationCallback = ssl.CreateRemoteCertificateValidator();

            if (ssl.RequiresRemoteCertificate)
            {
                var clientCertificate = ssl.LoadLocalCertificate()
                    ?? throw new InvalidOperationException(
                        $"WebSocket outbound endpoint ({endpoint}) has SSL mode Mutual but no client certificate is configured.");
                socket.Options.ClientCertificates.Add(clientCertificate);
            }
        }

        if (proxy.IsEnabled)
        {
            var proxyUri = new Uri($"http://{proxy.Host}:{proxy.Port}");
            var webProxy = new WebProxy(proxyUri);
            if (proxy.HasCredentials)
                webProxy.Credentials = new NetworkCredential(proxy.Username, proxy.Password);
            socket.Options.Proxy = webProxy;
        }

        await socket.ConnectAsync(endpoint, ct);
        return socket;
    }
}
