using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace Philips.IBE.IBEAgent.Endpoints.File;

// Windows-only: mounts a UNC share with explicit credentials (WNetAddConnection2) so the poller can
// read an authenticated network folder; WNetCancelConnection2 releases it on shutdown. EnsureConnected
// is idempotent and re-establishes a dropped connection. Credentials are never logged.
[SupportedOSPlatform("windows")]
public sealed class NetworkShareConnection : IDisposable
{
    private const int NoError = 0;
    private const int ErrorAlreadyAssigned = 85;
    private const int ResourceTypeDisk = 0x00000001;

    private readonly string _remotePath;
    private readonly FileShareCredential _credential;
    private readonly ILogger _logger;
    private bool _connected;

    public NetworkShareConnection(string remotePath, FileShareCredential credential, ILogger logger)
    {
        _remotePath = remotePath ?? throw new ArgumentNullException(nameof(remotePath));
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // Idempotent: re-issues the mount every call. An already-live connection returns ERROR_ALREADY_ASSIGNED
    // (treated as success); a dropped one is re-established. Only the first successful connect is logged.
    public void EnsureConnected()
    {
        var user = string.IsNullOrWhiteSpace(_credential.Domain)
            ? _credential.Username
            : $"{_credential.Domain}\\{_credential.Username}";
        var resource = new NetResource { Type = ResourceTypeDisk, RemoteName = _remotePath };

        var result = WNetAddConnection2(resource, _credential.Password, user, 0);
        if (result is not (NoError or ErrorAlreadyAssigned))
            throw new IOException($"Failed to connect to network share '{_remotePath}' (WNet error {result}).");

        if (!_connected)
        {
            _connected = true;
            _logger.LogInformation("Connected to network share {RemotePath}.", _remotePath);
        }
    }

    public void Dispose()
    {
        if (!_connected) return;
        _ = WNetCancelConnection2(_remotePath, 0, force: true);
        _connected = false;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class NetResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }

#pragma warning disable SYSLIB1054 // class-typed LPNETRESOURCE is not supported by [LibraryImport]; classic DllImport is required
    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(NetResource netResource, string? password, string? username, int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(string name, int flags, bool force);
#pragma warning restore SYSLIB1054
}
