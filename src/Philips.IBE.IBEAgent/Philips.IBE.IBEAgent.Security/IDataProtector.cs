namespace Philips.IBE.IBEAgent.Security;

// §3.9 — at-rest crypto seam for the store-and-forward buffer. Machine-scoped (DPAPI) per the
// architecture's "any multi-host split requires moving to a shared key/cert" note; the active
// Forward owner must run on the same machine that wrote the row.
public interface IDataProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext);
    byte[] Unprotect(ReadOnlySpan<byte> ciphertext);
}
