// IHttpSslPortBinder.cs
namespace Philips.IBE.IBEAgent.Security;

// DIP: HttpSslPortBinding depends on this abstraction, not the static HttpSslCertBinder,
// so bindings can be replaced (e.g. no-op in tests or alternative OS implementations).
public interface IHttpSslPortBinder
{
    void Bind(int port, System.Security.Cryptography.X509Certificates.X509Certificate2 certificate, bool negotiateClientCertificate = false);
    void Unbind(int port);
}
