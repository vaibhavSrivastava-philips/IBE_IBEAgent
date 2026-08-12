using System.Security.Cryptography.X509Certificates;

namespace Philips.IBE.IBEAgent.Security;

public interface ICertificateProvider
{
    X509Certificate2? LoadCertificate(CertificateReference? reference, bool requirePrivateKey = false);
}
