using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Philips.IBE.IBEAgent.Security;

// Programmatically binds / unbinds a server TLS certificate to an http.sys IP:port using the
// Windows HTTP Server API — identical to "netsh http add sslcert" but done at runtime in
// managed code so no manual setup or external script is needed.
//
// Why the original P/Invoke failed with error 87 (ERROR_INVALID_PARAMETER):
//   The original code used [MarshalAs(UnmanagedType.LPWStr)] on string fields inside a struct.
//   When Marshal.SizeOf / Marshal.StructureToPtr copies such a struct it embeds a managed string
//   pointer that becomes invalid the moment the GC moves the string. The fix is to use IntPtr
//   for every pointer field, allocate strings manually with Marshal.StringToHGlobalUni, and free
//   them after the call — the same pattern as raw Win32 C code.
//
// Requirements: the process must run as Administrator (or LocalSystem for a Windows Service).
public sealed class HttpSslCertBinder : IHttpSslPortBinder
{
    public static readonly IHttpSslPortBinder Instance = new HttpSslCertBinder();
    private HttpSslCertBinder() { }

    private static readonly Guid AppId = new("E8A1B2C3-D4E5-F6A7-B8C9-D0E1F2A3B4C5");

    // HTTP_SERVICE_CONFIG_SSL_FLAG_NEGOTIATE_CLIENT_CERT
    private const uint FlagNegotiateClientCert = 0x00000002;

    // ──────────────────────────────────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers <paramref name="certificate"/> as the server certificate for
    /// <paramref name="port"/> on all local interfaces (0.0.0.0). Idempotent: if the same
    /// thumbprint is already bound the call returns immediately; a stale binding is replaced.
    /// </summary>
    public void Bind(int port, X509Certificate2 certificate, bool negotiateClientCertificate = false)
    {
        var thumbBytes = HexToBytes(certificate.Thumbprint);
        var sockAddr   = BuildSockAddrIn(port);

        Initialize();
        try
        {
            if (QueryBinding(sockAddr, out var existing))
            {
                if (existing!.SequenceEqual(thumbBytes))
                    return;          // identical binding already present
                DeleteBinding(sockAddr);
            }

            SetBinding(sockAddr, thumbBytes, negotiateClientCertificate);
        }
        finally { Terminate(); }
    }

    /// <summary>
    /// Removes the http.sys SSL binding for <paramref name="port"/>.
    /// Safe to call when no binding exists.
    /// </summary>
    public void Unbind(int port)
    {
        var sockAddr = BuildSockAddrIn(port);
        Initialize();
        try { DeleteBinding(sockAddr); }
        finally { Terminate(); }
    }

    // ──────────────────────────────────────────────────────────────────────────────────────────
    // Windows HTTP Server API — P/Invoke
    // ──────────────────────────────────────────────────────────────────────────────────────────

    private const uint HttpInitializeConfig         = 0x00000002;
    private const uint HttpServiceConfigSslCertInfo = 1;
    private const uint NoError                      = 0;
    private const uint ErrorAlreadyExists           = 183;
    private const uint ErrorFileNotFound            = 2;

    [DllImport("httpapi.dll", SetLastError = false)]
    private static extern uint HttpInitialize(
        HTTPAPI_VERSION version, uint flags, IntPtr reserved);

    [DllImport("httpapi.dll", SetLastError = false)]
    private static extern uint HttpTerminate(uint flags, IntPtr reserved);

    [DllImport("httpapi.dll", SetLastError = false)]
    private static extern uint HttpSetServiceConfiguration(
        IntPtr serviceHandle, uint configId,
        IntPtr configInformation, int configInformationLength,
        IntPtr overlapped);

    [DllImport("httpapi.dll", SetLastError = false)]
    private static extern uint HttpDeleteServiceConfiguration(
        IntPtr serviceHandle, uint configId,
        IntPtr configInformation, int configInformationLength,
        IntPtr overlapped);

    [DllImport("httpapi.dll", SetLastError = false)]
    private static extern uint HttpQueryServiceConfiguration(
        IntPtr serviceHandle, uint configId,
        IntPtr inputConfigInfo, int inputConfigInfoLength,
        IntPtr outputConfigInfo, int outputConfigInfoLength,
        out int returnLength, IntPtr overlapped);

    // ──────────────────────────────────────────────────────────────────────────────────────────
    // Native structs
    //
    // All pointer-sized fields are declared as IntPtr (never [MarshalAs(LPWStr)]).
    // LayoutKind.Sequential lets the CLR insert the correct alignment padding so the layout
    // matches what the C compiler produces for the same struct on x86 and x64.
    // ──────────────────────────────────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct HTTPAPI_VERSION { public ushort Major, Minor; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HTTP_SERVICE_CONFIG_SSL_KEY
    {
        public IntPtr pIpPort;  // PSOCKADDR
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HTTP_SERVICE_CONFIG_SSL_PARAM
    {
        public int    SslHashLength;
        public IntPtr pSslHash;
        public Guid   AppId;
        public IntPtr pSslCertStoreName;
        public uint   DefaultCertCheckMode;
        public int    DefaultRevocationFreshnessTime;
        public int    DefaultRevocationUrlRetrievalTimeout;
        public IntPtr pDefaultSslCtlIdentifier;
        public IntPtr pDefaultSslCtlStoreName;
        public uint   DefaultFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HTTP_SERVICE_CONFIG_SSL_SET
    {
        public HTTP_SERVICE_CONFIG_SSL_KEY   KeyDesc;
        public HTTP_SERVICE_CONFIG_SSL_PARAM ParamDesc;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HTTP_SERVICE_CONFIG_SSL_QUERY
    {
        public uint                        QueryDesc;   // 0 = HttpServiceConfigQueryExact
        public HTTP_SERVICE_CONFIG_SSL_KEY KeyDesc;
        public uint                        dwToken;
    }

    // ──────────────────────────────────────────────────────────────────────────────────────────
    // Core operations
    // ──────────────────────────────────────────────────────────────────────────────────────────

    private static void Initialize()
    {
        var version = new HTTPAPI_VERSION { Major = 2, Minor = 0 };
        var result  = HttpInitialize(version, HttpInitializeConfig, IntPtr.Zero);
        if (result != NoError)
            throw new InvalidOperationException($"HttpInitialize failed with error {result}.");
    }

    private static void Terminate()
        => HttpTerminate(HttpInitializeConfig, IntPtr.Zero);

    private static void SetBinding(byte[] sockAddr, byte[] thumbprint, bool negotiateClientCert)
    {
        var hAddr  = GCHandle.Alloc(sockAddr,   GCHandleType.Pinned);
        var hThumb = GCHandle.Alloc(thumbprint, GCHandleType.Pinned);
        var storeNamePtr = Marshal.StringToHGlobalUni("MY");
        try
        {
            var cfg = new HTTP_SERVICE_CONFIG_SSL_SET
            {
                KeyDesc = new HTTP_SERVICE_CONFIG_SSL_KEY
                {
                    pIpPort = hAddr.AddrOfPinnedObject()
                },
                ParamDesc = new HTTP_SERVICE_CONFIG_SSL_PARAM
                {
                    SslHashLength     = thumbprint.Length,
                    pSslHash          = hThumb.AddrOfPinnedObject(),
                    AppId             = AppId,
                    pSslCertStoreName = storeNamePtr,
                    DefaultFlags      = negotiateClientCert ? FlagNegotiateClientCert : 0u
                }
            };

            int cfgSize = Marshal.SizeOf(cfg);
            var cfgPtr  = Marshal.AllocHGlobal(cfgSize);
            try
            {
                Marshal.StructureToPtr(cfg, cfgPtr, false);

                var result = HttpSetServiceConfiguration(
                    IntPtr.Zero, HttpServiceConfigSslCertInfo,
                    cfgPtr, cfgSize, IntPtr.Zero);

                if (result is not NoError and not ErrorAlreadyExists)
                    throw new InvalidOperationException(
                        $"HttpSetServiceConfiguration failed with error {result}. " +
                        "Ensure the process is running as Administrator.");
            }
            finally { Marshal.FreeHGlobal(cfgPtr); }
        }
        finally
        {
            Marshal.FreeHGlobal(storeNamePtr);
            hThumb.Free();
            hAddr.Free();
        }
    }

    private static void DeleteBinding(byte[] sockAddr)
    {
        var hAddr = GCHandle.Alloc(sockAddr, GCHandleType.Pinned);
        try
        {
            var cfg = new HTTP_SERVICE_CONFIG_SSL_SET
            {
                KeyDesc = new HTTP_SERVICE_CONFIG_SSL_KEY { pIpPort = hAddr.AddrOfPinnedObject() }
            };

            int cfgSize = Marshal.SizeOf(cfg);
            var cfgPtr  = Marshal.AllocHGlobal(cfgSize);
            try
            {
                Marshal.StructureToPtr(cfg, cfgPtr, false);

                var result = HttpDeleteServiceConfiguration(
                    IntPtr.Zero, HttpServiceConfigSslCertInfo,
                    cfgPtr, cfgSize, IntPtr.Zero);

                // ErrorFileNotFound = no existing binding — treat as success.
                if (result is not NoError and not ErrorFileNotFound)
                    throw new InvalidOperationException(
                        $"HttpDeleteServiceConfiguration failed with error {result}.");
            }
            finally { Marshal.FreeHGlobal(cfgPtr); }
        }
        finally { hAddr.Free(); }
    }

    private static bool QueryBinding(byte[] sockAddr, out byte[]? thumbprint)
    {
        thumbprint = null;
        var hAddr = GCHandle.Alloc(sockAddr, GCHandleType.Pinned);
        try
        {
            var query = new HTTP_SERVICE_CONFIG_SSL_QUERY
            {
                QueryDesc = 0,  // HttpServiceConfigQueryExact
                KeyDesc   = new HTTP_SERVICE_CONFIG_SSL_KEY { pIpPort = hAddr.AddrOfPinnedObject() }
            };

            int querySize = Marshal.SizeOf(query);
            var queryPtr  = Marshal.AllocHGlobal(querySize);
            try
            {
                Marshal.StructureToPtr(query, queryPtr, false);

                // First call: discover required output-buffer length.
                HttpQueryServiceConfiguration(
                    IntPtr.Zero, HttpServiceConfigSslCertInfo,
                    queryPtr, querySize,
                    IntPtr.Zero, 0, out int needed, IntPtr.Zero);

                if (needed == 0) return false;

                var outPtr = Marshal.AllocHGlobal(needed);
                try
                {
                    var result = HttpQueryServiceConfiguration(
                        IntPtr.Zero, HttpServiceConfigSslCertInfo,
                        queryPtr, querySize,
                        outPtr, needed, out _, IntPtr.Zero);

                    if (result == ErrorFileNotFound) return false;
                    if (result != NoError)
                        throw new InvalidOperationException(
                            $"HttpQueryServiceConfiguration failed with error {result}.");

                    var existing = Marshal.PtrToStructure<HTTP_SERVICE_CONFIG_SSL_SET>(outPtr);
                    int hashLen  = existing.ParamDesc.SslHashLength;
                    if (hashLen <= 0) return false;

                    thumbprint = new byte[hashLen];
                    Marshal.Copy(existing.ParamDesc.pSslHash, thumbprint, 0, hashLen);
                    return true;
                }
                finally { Marshal.FreeHGlobal(outPtr); }
            }
            finally { Marshal.FreeHGlobal(queryPtr); }
        }
        finally { hAddr.Free(); }
    }

    // ──────────────────────────────────────────────────────────────────────────────────────────
    // Utilities
    // ──────────────────────────────────────────────────────────────────────────────────────────

    // Hex string "A1B2C3..." → byte[] {0xA1, 0xB2, 0xC3, ...}
    private static byte[] HexToBytes(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    // Builds a SOCKADDR_IN byte array for 0.0.0.0:<port> — the key http.sys uses.
    private static byte[] BuildSockAddrIn(int port)
    {
        var ep    = new IPEndPoint(IPAddress.Any, port);
        var sa    = ep.Serialize();
        var bytes = new byte[sa.Size];
        for (int i = 0; i < sa.Size; i++) bytes[i] = sa[i];
        return bytes;
    }
}
