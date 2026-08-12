namespace Philips.IBE.IBEAgent.Service;

public interface ILicenseValidator
{
    LicenseValidationResult Validate(LicenseOptions options);
}
