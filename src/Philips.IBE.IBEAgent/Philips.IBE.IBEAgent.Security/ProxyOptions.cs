namespace Philips.IBE.IBEAgent.Security;

// Forward-proxy configuration for outbound endpoints (TCP and HTTP). Forward proxying is inherently
// an egress/client-side concern, so inbound endpoints do not consume this — only *Outbound options.
public sealed class ProxyOptions
{
    public bool IsEnabled { get; init; }

    // Proxy server address (host or host:port form is NOT expected here — see Port) e.g. "proxy.corp.local".
    public string? Host { get; init; }
    public int Port { get; init; }

    // Optional basic auth credentials. Leave both null/empty for an anonymous (no-credential) proxy.
    public string? Username { get; init; }
    public string? Password { get; init; }

    public bool HasCredentials => !string.IsNullOrEmpty(Username);
}
