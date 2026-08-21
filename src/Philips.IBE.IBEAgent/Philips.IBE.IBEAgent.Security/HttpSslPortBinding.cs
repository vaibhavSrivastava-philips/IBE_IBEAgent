using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Philips.IBE.IBEAgent.Security;

// Encapsulates the full lifecycle of binding a server TLS certificate to an http.sys port:
// load -> import to store -> bind -> unbind -> dispose.
//
// DIP: depends on IHttpTlsPortBinder (not the static HttpSslCertBinder) so the binding
// mechanism can be replaced in tests or on alternative platforms.
public sealed class HttpTlsPortBinding : IDisposable
{
    private readonly X509Certificate2 _cert;
    private readonly int _port;
    private readonly bool _negotiateClientCertificate;
    private readonly IHttpTlsPortBinder _binder;
    private bool _disposed;

    private HttpTlsPortBinding(X509Certificate2 cert, int port, bool negotiateClientCertificate, IHttpTlsPortBinder binder)
    {
        _cert = cert;
        _port = port;
        _negotiateClientCertificate = negotiateClientCertificate;
        _binder = binder;
    }

    /// <summary>
    /// Creates a binding scope for <paramref name="tls"/> if TLS is enabled; returns
    /// <c>null</c> when TLS is disabled so callers can treat the null case as "no TLS".
    /// Validates the prefix scheme and that a server certificate is configured.
    /// </summary>
    /// <param name="binder">
    /// Binder implementation to use. Pass <c>null</c> to use the production
    /// <see cref="HttpSslCertBinder.Instance"/> (Windows http.sys).
    /// </param>
    public static HttpTlsPortBinding? Create(
        TlsOptions tls,
        string prefix,
        int sourceEndpointId,
        string endpointLabel,
        IHttpTlsPortBinder? binder = null)
    {
        if (!tls.IsEnabled)
            return null;

        if (!prefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"{endpointLabel} inbound endpoint (source {sourceEndpointId}): TLS is enabled but Prefix '{prefix}' is not https://.");

        var cert = tls.LoadCertificate()
            ?? throw new InvalidOperationException(
                $"{endpointLabel} inbound endpoint (source {sourceEndpointId}): TLS is enabled but no server certificate is configured.");

        var port = new Uri(prefix).Port;
        return new HttpTlsPortBinding(cert, port, tls.RequiresRemoteCertificate, binder ?? HttpSslCertBinder.Instance);
    }

    /// <summary>Returns the port this binding is associated with.</summary>
    public int Port => _port;

    /// <summary>Returns the loaded server certificate.</summary>
    public X509Certificate2 Certificate => _cert;

    /// <summary>
    /// Imports the certificate into <c>LocalMachine\My</c> (so http.sys can locate it by
    /// thumbprint) and binds it to the port via the Windows HTTP Server API.
    /// </summary>
    public void Bind()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        if (!store.Certificates.Contains(_cert))
            store.Add(_cert);
        store.Close();

        _binder.Bind(_port, _cert, _negotiateClientCertificate);
    }

    /// <summary>
    /// Removes the http.sys port binding so the port is free for other processes.
    /// </summary>
    public void Unbind() => _binder.Unbind(_port);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cert.Dispose();
    }
}
