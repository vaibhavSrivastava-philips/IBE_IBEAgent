using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Philips.IBE.IBEAgent.Security;

// Composition helper: turns the transport-neutral TlsOptions into the primitives SslStream /
// SocketsHttpHandler / HttpListener actually need (X509Certificate2 + RemoteCertificateValidationCallback).
public static class TlsOptionsExtensions
{
    private static readonly ICertificateProvider DefaultProvider = new DefaultCertificateProvider();

    public static X509Certificate2? LoadCertificate(this TlsOptions options)
        => options.LoadCertificate(DefaultProvider);

    public static X509Certificate2? LoadCertificate(this TlsOptions options, ICertificateProvider certificateProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(certificateProvider);
        return certificateProvider.LoadCertificate(options.EffectiveLocalCertificate, requirePrivateKey: true);
    }

    public static X509Certificate2? LoadRootCertificate(this TlsOptions options)
        => options.LoadRootCertificate(DefaultProvider);

    public static X509Certificate2? LoadRootCertificate(this TlsOptions options, ICertificateProvider certificateProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(certificateProvider);
        return certificateProvider.LoadCertificate(options.EffectiveTrustedAuthority);
    }

    public static bool RequiresClientCertificate(this TlsOptions options)
        => options.RequireClientCertificate || options.Mode == TlsMode.Mutual;

    public static bool HasCertificate(this TlsOptions options)
        => options.EffectiveLocalCertificate is not null;

    public static bool HasRootCertificate(this TlsOptions options)
        => options.EffectiveTrustedAuthority is not null;

    // Validates the remote peer's certificate. When SkipCertificateValidation is true (dev/test),
    // all errors are ignored. When a RootCertificate is configured, the chain must build to that CA.
    public static RemoteCertificateValidationCallback CreateRemoteCertificateValidator(this TlsOptions options)
        => options.CreateRemoteCertificateValidator(DefaultProvider);

    public static RemoteCertificateValidationCallback CreateRemoteCertificateValidator(this TlsOptions options, ICertificateProvider certificateProvider)
    {
        var rootCert = options.LoadRootCertificate(certificateProvider);

        return (_, certificate, chain, errors) =>
        {
            if (options.SkipCertificateValidation) return true;

            if (rootCert is not null && certificate is not null)
            {
                using var customChain = new X509Chain();
                customChain.ChainPolicy.RevocationMode = options.CheckCertificateRevocation
                    ? X509RevocationMode.Online
                    : X509RevocationMode.NoCheck;
                customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                customChain.ChainPolicy.CustomTrustStore.Add(rootCert);

                return customChain.Build(new X509Certificate2(certificate));
            }

            return errors == SslPolicyErrors.None;
        };
    }
}
