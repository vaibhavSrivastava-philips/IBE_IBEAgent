namespace Philips.IBE.IBEAgent.Security;

public sealed class CertificateReference
{
    public string? StoreName { get; init; }
    public string? StoreLocation { get; init; }
    public string? Thumbprint { get; init; }
    public string? Subject { get; init; }
    public string? FriendlyName { get; init; }
}
