using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Philips.IBE.IBEAgent.TestKit;

// Generates throwaway self-signed certificates for SSL/TLS integration tests (TCP SslStream, HTTP
// SocketsHttpHandler). Never used for anything beyond in-memory / temp-file test scenarios.
public static class TestCertificateFactory
{
    public static X509Certificate2 CreateSelfSigned(string subjectName = "CN=ibe-agent-test")
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: false));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: false));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2")], critical: false)); // server + client auth

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(sanBuilder.Build());

        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        // Round-trip through PFX so the private key is exportable/reloadable the same way a file-backed
        // certificate loaded via X509CertificateLoader would be (matches production LoadLocalCertificate path).
        var pfxBytes = certificate.Export(X509ContentType.Pfx);
        return X509CertificateLoader.LoadPkcs12(pfxBytes, password: null, X509KeyStorageFlags.Exportable);
    }

    // Writes a self-signed certificate to a temp .pfx file (no password) and returns the path.
    // Caller is responsible for deleting the file (use in a try/finally or IDisposable test fixture).
    public static string CreateSelfSignedPfxFile(string subjectName = "CN=ibe-agent-test")
    {
        using var certificate = CreateSelfSigned(subjectName);
        var path = Path.Combine(Path.GetTempPath(), $"ibe-test-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx));
        return path;
    }
}
