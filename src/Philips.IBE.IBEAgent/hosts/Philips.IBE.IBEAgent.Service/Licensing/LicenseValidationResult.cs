namespace Philips.IBE.IBEAgent.Service;

public sealed record LicenseValidationResult(bool IsValid, string? Error = null)
{
    public static LicenseValidationResult Valid { get; } = new(true);
    public static LicenseValidationResult Invalid(string error) => new(false, error);
}
