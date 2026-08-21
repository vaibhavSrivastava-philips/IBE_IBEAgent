namespace Philips.IBE.IBEAgent.Security;

// Transport-neutral TLS configuration for an inbound or outbound endpoint leg. One shape reused
// by TCP (SslStream), HTTP (SocketsHttpHandler / HttpListener), and WebSocket so operators configure
// TLS the same way regardless of protocol.
public sealed class TlsOptions
{
    // Plain = plaintext. OneWay = TLS, server cert only. Mutual = mTLS, both sides present a cert.
    public TlsMode Mode { get; init; } = TlsMode.Plain;

    // TLS behavior is inferred from endpoint role + configured certificate material.
    public bool? Enabled { get; init; }
    public bool RequireClientCertificate { get; init; }

    // This endpoint's own certificate (server cert for inbound, client cert for outbound mTLS).
    public CertificateReference? Certificate { get; init; }

    // Root/CA certificate used to validate the remote peer's certificate chain.
    public CertificateReference? RootCertificate { get; init; }

    // Skip remote certificate validation — for development/test only. Never use in production.
    public bool SkipCertificateValidation { get; init; }

    public System.Security.Authentication.SslProtocols Protocols { get; init; }
        = System.Security.Authentication.SslProtocols.None; // None = OS negotiates best protocol

    public bool CheckCertificateRevocation { get; init; } = false;

    public bool IsEnabled => Enabled
        ?? Mode != TlsMode.Plain
        || RequireClientCertificate
        || Certificate is not null
        || RootCertificate is not null;

    public bool RequiresRemoteCertificate => RequireClientCertificate || Mode == TlsMode.Mutual;

    internal CertificateReference? EffectiveLocalCertificate => Certificate;
    internal CertificateReference? EffectiveTrustedAuthority => RootCertificate;
}
