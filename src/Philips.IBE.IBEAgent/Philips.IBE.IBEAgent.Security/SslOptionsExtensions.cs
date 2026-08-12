using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Philips.IBE.IBEAgent.Security;

// Composition helper: turns the transport-neutral SslOptions into the primitives SslStream /
// SocketsHttpHandler / HttpListener actually need (X509Certificate2 + RemoteCertificateValidationCallback).
public static class SslOptionsExtensions
{
    private static readonly ICertificateProvider DefaultProvider = new DefaultCertificateProvider();

    public static X509Certificate2? LoadLocalCertificate(this SslOptions options)
        => options.LoadLocalCertificate(DefaultProvider);

    public static X509Certificate2? LoadLocalCertificate(this SslOptions options, ICertificateProvider certificateProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(certificateProvider);
        return certificateProvider.LoadCertificate(options.EffectiveLocalCertificate, requirePrivateKey: true);
    }

    public static X509Certificate2? LoadTrustedAuthority(this SslOptions options)
        => options.LoadTrustedAuthority(DefaultProvider);

    public static X509Certificate2? LoadTrustedAuthority(this SslOptions options, ICertificateProvider certificateProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(certificateProvider);
        return certificateProvider.LoadCertificate(options.EffectiveTrustedAuthority);
    }

    public static bool RequiresClientCertificate(this SslOptions options)
        => options.RequireClientCertificate || options.Mode == SslMode.TwoWay;

    public static bool HasLocalCertificate(this SslOptions options)
        => options.EffectiveLocalCertificate is not null;

    public static bool HasTrustedAuthority(this SslOptions options)
        => options.EffectiveTrustedAuthority is not null;

    // Validates the remote peer's certificate: honours AllowUntrustedCertificate (dev/test), and,
    // when a TrustedCertificateAuthorityPath is configured, requires the chain to build to that CA
    // instead of relying solely on the machine trust store.
    public static RemoteCertificateValidationCallback CreateRemoteCertificateValidator(this SslOptions options)
        => options.CreateRemoteCertificateValidator(DefaultProvider);

    public static RemoteCertificateValidationCallback CreateRemoteCertificateValidator(this SslOptions options, ICertificateProvider certificateProvider)
    {
        var trustedCa = options.LoadTrustedAuthority(certificateProvider);

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
