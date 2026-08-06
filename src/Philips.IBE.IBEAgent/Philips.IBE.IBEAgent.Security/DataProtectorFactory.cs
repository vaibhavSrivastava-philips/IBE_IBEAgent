namespace Philips.IBE.IBEAgent.Security;

// Composition-root helper: DPAPI on Windows, the only supported deployment target (per §3.9).
// Non-Windows hosts are unsupported and fail fast — there is no production fallback protector.
public static class DataProtectorFactory
{
    public static IDataProtector Create()
    {
        if (!OperatingSystem.IsWindows())
        {
            // TODO(logging): emit a critical log here once this project references a logging seam.
            throw new PlatformNotSupportedException(
                "DPAPI at-rest protection requires Windows; the IBE Agent supports Windows hosts only.");
        }

        return new DpapiDataProtector();
    }
}
