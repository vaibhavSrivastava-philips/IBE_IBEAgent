using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Philips.IBE.IBEAgent.Security;

// §3.9 — DPAPI machine-scoped protector (replaces the legacy DataProtectionUtility for the
// engine's own store-and-forward payloads). Windows-only, matching ProtectedData semantics.
[SupportedOSPlatform("windows")]
public sealed class DpapiDataProtector : IDataProtector
{
    public byte[] Protect(ReadOnlySpan<byte> plaintext)
        => ProtectedData.Protect(plaintext.ToArray(), null, DataProtectionScope.LocalMachine);

    public byte[] Unprotect(ReadOnlySpan<byte> ciphertext)
        => ProtectedData.Unprotect(ciphertext.ToArray(), null, DataProtectionScope.LocalMachine);
}
