namespace Philips.IBE.IBEAgent.Security;

// Transport-neutral SSL/TLS configuration for an inbound or outbound endpoint leg. One shape reused
// by TCP (SslStream), HTTP (SocketsHttpHandler / HttpListener), and WebSocket so operators configure
// TLS the same way regardless of protocol.
//
// Production usage: always use LocalCertificate / TrustedCertificateAuthority with Kind=WindowsStore
// and a Subject (CN) or FriendlyName so certificate renewal requires no configuration change.
public sealed class SslOptions
{
    // Plain = plaintext. OneWay = TLS, server cert only. Mutual = mTLS, both sides present a cert.
    public SslMode Mode { get; init; } = SslMode.Plain;

    // TLS behavior is inferred from endpoint role + configured certificate material.
    public bool? Enabled { get; init; }
    public bool RequireClientCertificate { get; init; }

    // Server / client certificate for this side of the connection.
    // Use Kind=WindowsStore with Subject (CN) for production — survives renewal without config changes.
    public CertificateReference? LocalCertificate { get; init; }

    // Optional pinned CA/root used to validate the remote peer certificate.
    // Use Kind=WindowsStore with Subject (CN) for production.
    public CertificateReference? TrustedCertificateAuthority { get; init; }

    // Dev/test escape hatch: accept any remote certificate (chain errors ignored).
    // Must default to false so production configuration is secure-by-default.
    public bool AllowUntrustedCertificate { get; init; }

    public System.Security.Authentication.SslProtocols Protocols { get; init; }
        = System.Security.Authentication.SslProtocols.None; // None = OS negotiates best protocol

    public bool CheckCertificateRevocation { get; init; } = false;

    public bool IsEnabled => Enabled
        ?? Mode != SslMode.Plain
        || RequireClientCertificate
        || LocalCertificate is not null
        || TrustedCertificateAuthority is not null;

    public bool RequiresRemoteCertificate => RequireClientCertificate || Mode == SslMode.Mutual;

    internal CertificateReference? EffectiveLocalCertificate => LocalCertificate;
    internal CertificateReference? EffectiveTrustedAuthority => TrustedCertificateAuthority;
}
