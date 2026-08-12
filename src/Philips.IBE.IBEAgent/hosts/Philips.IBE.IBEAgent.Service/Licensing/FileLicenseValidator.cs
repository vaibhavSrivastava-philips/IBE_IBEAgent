using System.Text.Json;

namespace Philips.IBE.IBEAgent.Service;

public sealed class FileLicenseValidator : ILicenseValidator
{
    public LicenseValidationResult Validate(LicenseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
            return LicenseValidationResult.Valid;

        if (string.IsNullOrWhiteSpace(options.Path))
            return LicenseValidationResult.Invalid("License validation is enabled, but License:Path is not configured.");

        if (!File.Exists(options.Path))
            return LicenseValidationResult.Invalid($"License file '{options.Path}' was not found.");

        LicenseDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<LicenseDocument>(File.ReadAllText(options.Path), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException ex)
        {
            return LicenseValidationResult.Invalid($"License file '{options.Path}' is not valid JSON: {ex.GetType().Name}.");
        }

        if (document is null)
            return LicenseValidationResult.Invalid($"License file '{options.Path}' is empty.");

        if (!string.Equals(document.Product, options.Product, StringComparison.OrdinalIgnoreCase))
            return LicenseValidationResult.Invalid($"License product '{document.Product}' does not match required product '{options.Product}'.");

        if (document.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            return LicenseValidationResult.Invalid("License is expired.");

        if (options.RequireSignature && string.IsNullOrWhiteSpace(document.Signature))
            return LicenseValidationResult.Invalid("License signature is required but missing.");

        return LicenseValidationResult.Valid;
    }

    private sealed record LicenseDocument
    {
        public string? Product { get; init; }
        public DateTimeOffset ExpiresAtUtc { get; init; }
        public string? Signature { get; init; }
    }
}
