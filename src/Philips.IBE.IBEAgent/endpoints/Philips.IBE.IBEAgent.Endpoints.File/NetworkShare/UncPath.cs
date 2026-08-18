namespace Philips.IBE.IBEAgent.Endpoints.File;

// UNC helpers for network-share mounting. A share directory may be written with either separator
// ("\\server\share" or "//server/share") — Windows file APIs accept both, so we honor both for
// config/legacy parity — but WNetAddConnection2 requires the backslash form.
public static class UncPath
{
    public static bool IsUnc(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal);

    // Backslash remote name WNetAddConnection2 expects. Only meaningful for a UNC path.
    public static string ToRemoteName(string path) => path.Replace('/', '\\');
}
