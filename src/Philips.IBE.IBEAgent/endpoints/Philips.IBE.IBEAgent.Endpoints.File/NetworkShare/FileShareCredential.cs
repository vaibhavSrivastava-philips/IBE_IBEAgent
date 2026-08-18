namespace Philips.IBE.IBEAgent.Endpoints.File;

// Plaintext credentials for mounting an authenticated UNC file share. The host decrypts the
// DPAPI-protected password (via the Security IDataProtector) before constructing this — the endpoint
// never sees the protected form, and the password is never logged.
public sealed record FileShareCredential(string Username, string? Domain, string Password);
