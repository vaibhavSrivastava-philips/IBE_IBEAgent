using System.Security.Cryptography.X509Certificates;

namespace Philips.IBE.IBEAgent.Security;

// OCP: one loader per CertificateReferenceKind. Register a custom implementation in
// DefaultCertificateProvider to support new kinds without modifying existing code.
public interface ICertificateLoader
{
    X509Certificate2? Load(CertificateReference reference);
}
