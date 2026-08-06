namespace Philips.IBE.IBEAgent.Security;

// Transport-neutral SSL/TLS configuration for an inbound or outbound endpoint leg. One shape reused
// by TCP (SslStream) and HTTP (SocketsHttpHandler / HttpListener) so operators configure TLS the
// same way regardless of protocol.
public sealed class SslOptions
{
    // None = plaintext. OneWay = TLS, only the remote peer's certificate is validated (typical
    // client-facing web/API pattern). TwoWay = mutual TLS: both sides present + validate a certificate.
    public SslMode Mode { get; init; } = SslMode.None;

    // Certificate presented by *this* side of the connection:
    //  - inbound (server) endpoint: required for OneWay and TwoWay.
    //  - outbound (client) endpoint: required for TwoWay only (client authentication).
    public string? CertificatePath { get; init; }
    public string? CertificatePassword { get; init; }

    // Optional pinned CA/root used to validate the *remote* peer's certificate instead of (or in
    // addition to) the machine trust store — useful for private/self-signed PKI in the field.
    public string? TrustedCertificateAuthorityPath { get; init; }

    // Dev/test escape hatch: accept any remote certificate (chain errors ignored). Must default to
    // false so production configuration is secure-by-default.
    public bool AllowUntrustedCertificate { get; init; }

    public System.Security.Authentication.SslProtocols Protocols { get; init; }
        = System.Security.Authentication.SslProtocols.None; // None = let the OS negotiate the best supported protocol

    public bool CheckCertificateRevocation { get; init; } = true;

    public bool IsEnabled => Mode != SslMode.None;
    public bool RequiresRemoteCertificate => Mode == SslMode.TwoWay;
}
