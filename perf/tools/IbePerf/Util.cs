using System.Diagnostics;
using System.Globalization;

namespace IbePerf;

// Minimal --key value argument parser (no external dependency for portability).
internal sealed class Cli
{
    private readonly Dictionary<string, string> _map;
    private Cli(Dictionary<string, string> map) => _map = map;

    public static Cli Parse(ReadOnlySpan<string> args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--", StringComparison.Ordinal)) continue;
            var key = a[2..];
            var val = "true";
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                val = args[i + 1];
                i++;
            }
            map[key] = val;
        }
        return new Cli(map);
    }

    public string Get(string key) => _map.TryGetValue(key, out var v) ? v : throw new ArgumentException($"missing --{key}");
    public string Get(string key, string fallback) => _map.TryGetValue(key, out var v) ? v : fallback;
    public bool Has(string key) => _map.ContainsKey(key);
}

// QueryPerformanceCounter-based clock. Raw ticks are comparable ACROSS processes on the same machine
// (same frequency, boot-time origin), so load-gen send ticks and sink receive ticks can be subtracted.
internal static class Qpc
{
    public static long Now() => Stopwatch.GetTimestamp();
    public static double ToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}

// MLLP framing: 0x0B <payload> 0x1C 0x0D.
internal static class Mllp
{
    private const byte Vt = 0x0B, Fs = 0x1C, Cr = 0x0D;

    public static byte[] Frame(ReadOnlySpan<byte> body)
    {
        var buf = new byte[body.Length + 3];
        buf[0] = Vt;
        body.CopyTo(buf.AsSpan(1));
        buf[^2] = Fs;
        buf[^1] = Cr;
        return buf;
    }

    // Reads one MLLP frame (payload without VT/FS/CR); null at end of stream. Wrap the network stream
    // in a BufferedStream so the byte-at-a-time scan does not cause a syscall per byte.
    public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream(256);
        var one = new byte[1];
        var started = false;
        while (true)
        {
            var n = await stream.ReadAsync(one, ct);
            if (n == 0) return started ? ms.ToArray() : null;
            var b = one[0];
            if (!started)
            {
                if (b == Vt) started = true;
                continue;
            }
            if (b == Fs)
            {
                await stream.ReadAsync(one, ct); // consume trailing CR
                return ms.ToArray();
            }
            ms.WriteByte(b);
        }
    }
}

internal static class Stats
{
    // Linear-interpolated percentile over an ascending-sorted array.
    public static double Percentile(double[] sortedAsc, double p)
    {
        if (sortedAsc.Length == 0) return double.NaN;
        if (sortedAsc.Length == 1) return sortedAsc[0];
        var rank = p / 100.0 * (sortedAsc.Length - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        if (lo == hi) return sortedAsc[lo];
        return sortedAsc[lo] + (sortedAsc[hi] - sortedAsc[lo]) * (rank - lo);
    }

    public static string F(double v, int decimals = 2) =>
        double.IsNaN(v) ? "-" : v.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}
