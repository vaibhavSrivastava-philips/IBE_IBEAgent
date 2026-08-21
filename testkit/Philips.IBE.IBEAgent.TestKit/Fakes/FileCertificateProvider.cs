using System.Security.Cryptography.X509Certificates;
using Philips.IBE.IBEAgent.Security;

namespace Philips.IBE.IBEAgent.TestKit;

// Test-only ICertificateProvider that loads certificates from file paths.
// Used by integration tests that create temp pfx files with TestCertificateFactory.
public sealed class FileCertificateProvider : ICertificateProvider
{
    private readonly string _path;
    private readonly string? _password;

    public FileCertificateProvider(string path, string? password = null)
    {
        _path = path;
        _password = password;
    }

    public X509Certificate2? LoadCertificate(CertificateReference? reference, bool requirePrivateKey = false)
        => X509CertificateLoader.LoadPkcs12FromFile(_path, _password);
}
