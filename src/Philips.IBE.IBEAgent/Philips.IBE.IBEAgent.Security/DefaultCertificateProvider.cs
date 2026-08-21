using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Philips.IBE.IBEAgent.Security;

public sealed class DefaultCertificateProvider : ICertificateProvider
{
    private readonly ICertificateLoader _loader;

    public DefaultCertificateProvider() : this(new WindowsStoreCertificateLoader()) { }

    public DefaultCertificateProvider(ICertificateLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;
    }

    public X509Certificate2? LoadCertificate(CertificateReference? reference, bool requirePrivateKey = false)
    {
        if (reference is null) return null;

        var certificate = _loader.Load(reference);

        if (certificate is not null && requirePrivateKey && !certificate.HasPrivateKey)
            throw new InvalidOperationException("Configured certificate does not include a private key.");

        return certificate;
    }
}

internal sealed class WindowsStoreCertificateLoader : ICertificateLoader
{
    public X509Certificate2? Load(CertificateReference reference)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("WindowsStore certificate references are only supported on Windows.");

        var storeName = Enum.TryParse<StoreName>(reference.StoreName, ignoreCase: true, out var parsedStoreName)
            ? parsedStoreName
            : StoreName.My;
        var storeLocation = Enum.TryParse<StoreLocation>(reference.StoreLocation, ignoreCase: true, out var parsedStoreLocation)
            ? parsedStoreLocation
            : StoreLocation.LocalMachine;

        using var store = new X509Store(storeName, storeLocation);
        store.Open(OpenFlags.ReadOnly);

        var matches = store.Certificates
            .Find(X509FindType.FindByTimeValid, DateTime.Now, validOnly: false)
            .OfType<X509Certificate2>()
            .Where(c => Matches(c, reference))
            .ToList();

        return matches.Count switch
        {
            0 => throw new InvalidOperationException(
                "No certificate matched the configured Windows certificate store reference."),
            1 => matches[0],
            _ => throw new InvalidOperationException(
                "Multiple certificates matched the configured Windows certificate store reference; make it more specific."),
        };
    }

    private static bool Matches(X509Certificate2 certificate, CertificateReference reference)
    {
        var hasFilter = false;

        if (!string.IsNullOrWhiteSpace(reference.Thumbprint))
        {
            hasFilter = true;
            if (!string.Equals(NormalizeThumbprint(certificate.Thumbprint),
                    NormalizeThumbprint(reference.Thumbprint), StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(reference.Subject))
        {
            hasFilter = true;
            if (!certificate.Subject.Contains(reference.Subject, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(reference.FriendlyName))
        {
            hasFilter = true;
            if (!string.Equals(certificate.FriendlyName, reference.FriendlyName, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return hasFilter;
    }

    private static string NormalizeThumbprint(string? thumbprint)
        => (thumbprint ?? string.Empty)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(":", string.Empty, StringComparison.Ordinal);
}
