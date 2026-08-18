using System.Globalization;
using System.Text;
using System.Text.Json;

namespace IbePerf;

// Builds a single self-contained HTML report (inline CSS + inline SVG charts, no external/CDN assets
// so it renders offline on an air-gapped test server). Reads sysinfo.json + slo.json at the session
// root and each <scenario>/summary.json (+ sink.json, counters.csv). Optional baseline diff.
internal static class ReportVerb
{
    private sealed record Row(
        string Name, string Proto, string Target, string Mode, int Connections,
        int Measured, double ThroughputMsgSec, double ThroughputMbSec,
        double P50, double P95, double P99, double P999, double Max, double Mean,
        long Sent, long Distinct, long Duplicates, long OutOfOrder, long Nacks,
        double? Cpu, double? AllocMbSec, double? PctGc);

    public static int Run(Cli cli)
    {
        var session = cli.Get("session");
        var outFile = cli.Get("out", Path.Combine(session, "session.html"));
        var baseline = cli.Get("baseline", "");

        var sys = ReadJson(Path.Combine(session, "sysinfo.json"));
        var slo = ReadJson(Path.Combine(session, "slo.json"));
        var rows = LoadRows(session);
        var baseRows = string.IsNullOrEmpty(baseline) ? new() : LoadRows(baseline);

        var html = BuildHtml(sys, slo, rows, baseRows);
        File.WriteAllText(outFile, html);
        Console.WriteLine($"[report] wrote {outFile} ({rows.Count} scenario(s))");
        return 0;
    }

    private static List<Row> LoadRows(string session)
    {
        var rows = new List<Row>();
        foreach (var dir in Directory.GetDirectories(session).OrderBy(d => d))
        {
            var sPath = Path.Combine(dir, "summary.json");
            if (!File.Exists(sPath)) continue;
            using var s = JsonDocument.Parse(File.ReadAllText(sPath));
            var r = s.RootElement;
            var rtt = r.GetProperty("rttMs");

            long distinct = 0, dupes = 0, ooo = 0, nacks = 0;
            var sinkPath = Path.Combine(dir, "sink.json");
            if (File.Exists(sinkPath))
            {
                using var sk = JsonDocument.Parse(File.ReadAllText(sinkPath));
                var se = sk.RootElement;
                distinct = GetLong(se, "distinct");
                dupes = GetLong(se, "duplicates");
                ooo = GetLong(se, "outOfOrder");
                nacks = GetLong(se, "nacksSent");
            }

            var (cpu, alloc, pctGc) = ReadCounters(Path.Combine(dir, "counters.csv"));

            rows.Add(new Row(
                r.GetProperty("scenario").GetString() ?? Path.GetFileName(dir),
                r.GetProperty("proto").GetString() ?? "",
                r.GetProperty("target").GetString() ?? "",
                r.GetProperty("mode").GetString() ?? "",
                r.GetProperty("connections").GetInt32(),
                r.GetProperty("measured").GetInt32(),
                r.GetProperty("throughputMsgSec").GetDouble(),
                r.GetProperty("throughputMbSec").GetDouble(),
                GetD(rtt, "p50"), GetD(rtt, "p95"), GetD(rtt, "p99"), GetD(rtt, "p999"), GetD(rtt, "max"), GetD(rtt, "mean"),
                r.GetProperty("sent").GetInt64(), distinct, dupes, ooo, nacks,
                cpu, alloc, pctGc));
        }
        return rows;
    }

    private static (double? Cpu, double? Alloc, double? PctGc) ReadCounters(string path)
    {
        if (!File.Exists(path)) return (null, null, null);
        // dotnet-counters CSV export; column order varies by version, so match the counter name anywhere
        // in the line and take the last numeric token as the value (keeps the latest sample).
        double? cpu = null, alloc = null, gc = null;
        foreach (var line in File.ReadLines(path))
        {
            var lower = line.ToLowerInvariant();
            var parts = line.Split(',');
            double? val = null;
            for (var i = parts.Length - 1; i >= 0; i--)
                if (double.TryParse(parts[i].Trim().Trim('"'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) { val = v; break; }
            if (val is null) continue;
            if (lower.Contains("cpu usage")) cpu = val;
            else if (lower.Contains("allocation rate")) alloc = val.Value / 1_000_000.0;
            else if (lower.Contains("% time in gc") || lower.Contains("time in gc")) gc = val;
        }
        return (cpu, alloc, gc);
    }

    private static string BuildHtml(JsonDocument? sys, JsonDocument? slo, List<Row> rows, List<Row> baseRows)
    {
        double sloP99 = slo is not null && slo.RootElement.TryGetProperty("p99RoundTripMs", out var p) ? p.GetDouble() : double.NaN;
        bool sloZeroLoss = slo is not null && slo.RootElement.TryGetProperty("zeroLoss", out var z) && z.GetBoolean();

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset='utf-8'><title>IBE Agent Performance Report</title><style>");
        sb.Append("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1b1b1b;background:#fafafa}");
        sb.Append("h1{margin:0 0 4px}h2{margin-top:32px;border-bottom:2px solid #ddd;padding-bottom:4px}");
        sb.Append("table{border-collapse:collapse;width:100%;margin:12px 0;background:#fff}");
        sb.Append("th,td{border:1px solid #ddd;padding:6px 10px;text-align:right;font-size:13px}th{background:#f0f4f8;text-align:center}");
        sb.Append("td.l,th.l{text-align:left}.pass{color:#0a7d29;font-weight:600}.fail{color:#c0202a;font-weight:600}");
        sb.Append(".muted{color:#666;font-size:12px}.card{background:#fff;border:1px solid #e0e0e0;border-radius:6px;padding:12px 16px;margin:8px 0}");
        sb.Append("</style></head><body>");
        sb.Append("<h1>IBE Agent — Performance Report</h1>");
        sb.Append($"<div class='muted'>Generated {DateTimeOffset.Now.ToString("u", CultureInfo.InvariantCulture)}</div>");

        AppendSysInfo(sb, sys);

        // ---- summary table ----
        sb.Append("<h2>Scenario summary</h2><table><tr>");
        foreach (var h in new[] { "Scenario", "Proto", "Mode", "Conn", "Msgs", "msg/s", "MB/s", "p50", "p95", "p99", "p99.9", "max", "mean", "Loss", "Dup", "OoO", "NACK", "CPU%", "Alloc MB/s", "%GC", "SLO" })
            sb.Append(h is "Scenario" or "Proto" or "Mode" ? $"<th class='l'>{h}</th>" : $"<th>{h}</th>");
        sb.Append("</tr>");
        foreach (var r in rows)
        {
            var loss = r.Sent - r.Distinct;
            var lossPct = r.Sent > 0 ? 100.0 * loss / r.Sent : 0;
            var sloOk = (double.IsNaN(sloP99) || r.P99 <= sloP99) && (!sloZeroLoss || loss <= 0);
            sb.Append("<tr>");
            sb.Append($"<td class='l'>{r.Name}</td><td class='l'>{r.Proto}</td><td class='l'>{r.Mode}</td><td>{r.Connections}</td>");
            sb.Append($"<td>{r.Measured}</td><td>{Stats.F(r.ThroughputMsgSec)}</td><td>{Stats.F(r.ThroughputMbSec, 3)}</td>");
            sb.Append($"<td>{Stats.F(r.P50)}</td><td>{Stats.F(r.P95)}</td><td>{Stats.F(r.P99)}</td><td>{Stats.F(r.P999)}</td><td>{Stats.F(r.Max)}</td><td>{Stats.F(r.Mean)}</td>");
            sb.Append($"<td class='{(loss > 0 ? "fail" : "")}'>{loss} ({Stats.F(lossPct)}%)</td><td>{r.Duplicates}</td><td>{r.OutOfOrder}</td><td>{r.Nacks}</td>");
            sb.Append($"<td>{(r.Cpu is null ? "-" : Stats.F(r.Cpu.Value))}</td><td>{(r.AllocMbSec is null ? "-" : Stats.F(r.AllocMbSec.Value))}</td><td>{(r.PctGc is null ? "-" : Stats.F(r.PctGc.Value))}</td>");
            sb.Append($"<td class='{(sloOk ? "pass" : "fail")}'>{(sloOk ? "PASS" : "FAIL")}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</table>");
        if (!double.IsNaN(sloP99)) sb.Append($"<div class='muted'>SLO: p99 &le; {Stats.F(sloP99)} ms{(sloZeroLoss ? ", zero message loss" : "")}.</div>");

        // ---- charts ----
        sb.Append("<h2>Throughput (msg/s)</h2>");
        sb.Append(BarChart(rows.Select(r => (r.Name, r.ThroughputMsgSec)).ToList(), "#2b7bba"));
        sb.Append("<h2>Latency p99 (ms)</h2>");
        sb.Append(BarChart(rows.Select(r => (r.Name, r.P99)).ToList(), "#c0202a"));

        // ---- baseline diff ----
        if (baseRows.Count > 0)
        {
            sb.Append("<h2>Baseline comparison</h2><table><tr><th class='l'>Scenario</th><th>msg/s Δ%</th><th>p99 Δ%</th></tr>");
            foreach (var r in rows)
            {
                var b = baseRows.Find(x => x.Name == r.Name);
                if (b is null) continue;
                var tp = b.ThroughputMsgSec > 0 ? 100.0 * (r.ThroughputMsgSec - b.ThroughputMsgSec) / b.ThroughputMsgSec : 0;
                var lp = b.P99 > 0 ? 100.0 * (r.P99 - b.P99) / b.P99 : 0;
                sb.Append($"<tr><td class='l'>{r.Name}</td><td class='{(tp < -5 ? "fail" : "pass")}'>{Stats.F(tp)}</td><td class='{(lp > 5 ? "fail" : "pass")}'>{Stats.F(lp)}</td></tr>");
            }
            sb.Append("</table>");
        }

        sb.Append("<div class='muted' style='margin-top:32px'>Raw per-message data is in each scenario's <code>latencies.csv</code> / <code>sink.csv</code>; the agent log for each scenario is under its <code>logs/</code> folder.</div>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void AppendSysInfo(StringBuilder sb, JsonDocument? sys)
    {
        sb.Append("<div class='card'>");
        if (sys is null) { sb.Append("<span class='muted'>No sysinfo.json.</span></div>"); return; }
        var e = sys.RootElement;
        void Kv(string label, string prop)
        {
            if (e.TryGetProperty(prop, out var v))
                sb.Append($"<b>{label}:</b> {v.ToString()} &nbsp; ");
        }
        Kv("Machine", "machine"); Kv("CPU", "cpu"); Kv("Cores", "cores"); Kv("RAM(GB)", "ramGb");
        sb.Append("<br>");
        Kv("OS", "os"); Kv(".NET", "dotnet"); Kv("GC", "gcMode"); Kv("Server GC", "serverGc");
        sb.Append("<br>");
        Kv("Git", "gitBranch"); Kv("Commit", "gitCommit"); Kv("Contract", "contract");
        sb.Append("</div>");
    }

    // Simple horizontal SVG bar chart, no external assets.
    private static string BarChart(List<(string Label, double Value)> data, string color)
    {
        if (data.Count == 0) return "<div class='muted'>no data</div>";
        var max = data.Max(d => double.IsNaN(d.Value) ? 0 : d.Value);
        if (max <= 0) max = 1;
        const int rowH = 26, w = 900, labelW = 190, barMax = 620;
        var h = data.Count * rowH + 10;
        var sb = new StringBuilder();
        sb.Append($"<svg width='{w}' height='{h}' xmlns='http://www.w3.org/2000/svg'>");
        var y = 6;
        foreach (var (label, value) in data)
        {
            var v = double.IsNaN(value) ? 0 : value;
            var bw = (int)(barMax * v / max);
            sb.Append($"<text x='0' y='{y + 16}' font-size='12' font-family='Arial'>{Escape(label)}</text>");
            sb.Append($"<rect x='{labelW}' y='{y + 4}' width='{bw}' height='16' fill='{color}' rx='2'/>");
            sb.Append($"<text x='{labelW + bw + 6}' y='{y + 16}' font-size='12' font-family='Arial'>{Stats.F(v)}</text>");
            y += rowH;
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    private static JsonDocument? ReadJson(string path) => File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path)) : null;
    private static double GetD(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN;
    private static long GetLong(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
}
