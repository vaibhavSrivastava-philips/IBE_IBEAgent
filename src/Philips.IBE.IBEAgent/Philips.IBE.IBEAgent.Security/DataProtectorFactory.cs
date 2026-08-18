namespace Philips.IBE.IBEAgent.Security;

// Composition-root helper: DPAPI on Windows, the only supported deployment target (per §3.9).
// Non-Windows hosts are unsupported and fail fast — there is no production fallback protector.
public static class DataProtectorFactory
{
    public static IDataProtector Create()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "DPAPI at-rest protection requires Windows; the IBE Agent supports Windows hosts only.");
        }

        return new DpapiDataProtector();
    }
}
