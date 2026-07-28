using System.Runtime.InteropServices;

namespace Philips.IBE.IBEAgent.Security;

// Composition-root helper: DPAPI on Windows (the deployment target, per §3.9), the reversible
// fallback elsewhere (dev/test only).
public static class DataProtectorFactory
{
    public static IDataProtector Create()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new DpapiDataProtector()
            : new NullDataProtector();
}
