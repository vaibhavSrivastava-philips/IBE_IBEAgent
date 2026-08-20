using System.Collections.Frozen;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Philips.IBE.IBEAgent.Security;

// OCP: add support for a new CertificateReferenceKind by registering an ICertificateLoader
// in the loaders dictionary — no modification to this class required.
public sealed class DefaultCertificateProvider : ICertificateProvider
{
    private readonly FrozenDictionary<CertificateReferenceKind, ICertificateLoader> _loaders;

    // Default constructor: registers the built-in loaders for all supported kinds.
    public DefaultCertificateProvider()
        : this(new Dictionary<CertificateReferenceKind, ICertificateLoader>
        {
            [CertificateReferenceKind.File]          = new FileCertificateLoader(),
            [CertificateReferenceKind.MountedSecret] = new FileCertificateLoader(),
            [CertificateReferenceKind.WindowsStore]  = new WindowsStoreCertificateLoader(),
            [CertificateReferenceKind.LinuxStore]    = new LinuxStoreCertificateLoader(),
        })
    { }

    // Extensibility constructor: supply a custom loader map (e.g. add CloudKeyVault without modifying this class).
    public DefaultCertificateProvider(IReadOnlyDictionary<CertificateReferenceKind, ICertificateLoader> loaders)
    {
        ArgumentNullException.ThrowIfNull(loaders);
        _loaders = loaders.ToFrozenDictionary();
    }

    public X509Certificate2? LoadCertificate(CertificateReference? reference, bool requirePrivateKey = false)
    {
        if (reference is null) return null;

        if (!_loaders.TryGetValue(reference.Kind, out var loader))
            throw new InvalidOperationException($"Unsupported certificate reference kind '{reference.Kind}'. " +
                "Register a custom ICertificateLoader to support it.");

        var certificate = loader.Load(reference);

        if (certificate is not null && requirePrivateKey && !certificate.HasPrivateKey)
            throw new InvalidOperationException("Configured certificate does not include a private key.");

        return certificate;
    }
}

// -- Built-in loaders --------------------------------------------------------------------------

internal sealed class FileCertificateLoader : ICertificateLoader
{
    public X509Certificate2? Load(CertificateReference reference)
    {
        var path = reference.Path ?? reference.CertificatePath;
        if (string.IsNullOrWhiteSpace(path)) return null;

        var extension = Path.GetExtension(path);
        var isPkcs12 = extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".p12", StringComparison.OrdinalIgnoreCase);

        return isPkcs12 || !string.IsNullOrEmpty(reference.Password)
            ? X509CertificateLoader.LoadPkcs12FromFile(path, reference.Password)
            : X509CertificateLoader.LoadCertificateFromFile(path);
    }
}

internal sealed class LinuxStoreCertificateLoader : ICertificateLoader
{
    public X509Certificate2? Load(CertificateReference reference)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            throw new PlatformNotSupportedException("LinuxStore certificate references are only supported on Linux.");
        return new FileCertificateLoader().Load(reference);
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
