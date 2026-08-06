using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Philips.IBE.IBEAgent.Security;

// Composition helper: turns the transport-neutral SslOptions into the primitives SslStream /
// SocketsHttpHandler / HttpListener actually need (X509Certificate2 + RemoteCertificateValidationCallback).
public static class SslOptionsExtensions
{
    public static X509Certificate2? LoadLocalCertificate(this SslOptions options)
    {
        if (string.IsNullOrEmpty(options.CertificatePath)) return null;

        // PKCS#12 containers (.pfx/.p12) carry the private key and must go through
        // LoadPkcs12FromFile even when there's no password; LoadCertificateFromFile only
        // understands public-key-only formats (DER/PEM) and would fail on a PFX.
        var extension = Path.GetExtension(options.CertificatePath);
        var isPkcs12 = extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".p12", StringComparison.OrdinalIgnoreCase);

        return isPkcs12 || !string.IsNullOrEmpty(options.CertificatePassword)
            ? X509CertificateLoader.LoadPkcs12FromFile(options.CertificatePath, options.CertificatePassword)
            : X509CertificateLoader.LoadCertificateFromFile(options.CertificatePath);
    }

    public static X509Certificate2? LoadTrustedAuthority(this SslOptions options)
        => string.IsNullOrEmpty(options.TrustedCertificateAuthorityPath)
            ? null
            : X509CertificateLoader.LoadCertificateFromFile(options.TrustedCertificateAuthorityPath);

    // Validates the remote peer's certificate: honours AllowUntrustedCertificate (dev/test), and,
    // when a TrustedCertificateAuthorityPath is configured, requires the chain to build to that CA
    // instead of relying solely on the machine trust store.
    public static RemoteCertificateValidationCallback CreateRemoteCertificateValidator(this SslOptions options)
    {
        var trustedCa = options.LoadTrustedAuthority();

        return (_, certificate, chain, errors) =>
        {
            if (options.AllowUntrustedCertificate) return true;

            if (trustedCa is not null && certificate is not null)
            {
                using var customChain = new X509Chain();
                customChain.ChainPolicy.RevocationMode = options.CheckCertificateRevocation
                    ? X509RevocationMode.Online
                    : X509RevocationMode.NoCheck;
                customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                customChain.ChainPolicy.CustomTrustStore.Add(trustedCa);

                return customChain.Build(new X509Certificate2(certificate));
            }

            return errors == SslPolicyErrors.None;
        };
    }
}
