namespace Philips.IBE.IBEAgent.Security;

public enum CertificateReferenceKind
{
    File,
    WindowsStore,
    LinuxStore,
    MountedSecret,
}

public sealed class CertificateReference
{
    public CertificateReferenceKind Kind { get; init; } = CertificateReferenceKind.File;
    public string? Path { get; init; }
    public string? Password { get; init; }
    public string? StoreName { get; init; }
    public string? StoreLocation { get; init; }
    public string? Thumbprint { get; init; }
    public string? Subject { get; init; }
    public string? FriendlyName { get; init; }
    public string? CertificatePath { get; init; }
    public string? PrivateKeyPath { get; init; }
}
