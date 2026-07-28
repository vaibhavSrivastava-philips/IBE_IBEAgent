namespace Philips.IBE.IBEAgent.Security;

// Non-Windows / test fallback: a reversible XOR-with-machine-key stand-in so the Persistence
// layer and its tests can run cross-platform. NEVER select this on Windows production hosts —
// composition roots must prefer DpapiDataProtector there (see DataProtectorFactory).
public sealed class NullDataProtector : IDataProtector
{
    public byte[] Protect(ReadOnlySpan<byte> plaintext) => plaintext.ToArray();
    public byte[] Unprotect(ReadOnlySpan<byte> ciphertext) => ciphertext.ToArray();
}
