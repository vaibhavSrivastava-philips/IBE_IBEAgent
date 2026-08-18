namespace Philips.IBE.IBEAgent.Service;

public sealed record LicenseOptions
{
    public bool Enabled { get; init; }
    public string? Path { get; init; }
    public string Product { get; init; } = "IBEAgent";
    public bool RequireSignature { get; init; } = true;
}
