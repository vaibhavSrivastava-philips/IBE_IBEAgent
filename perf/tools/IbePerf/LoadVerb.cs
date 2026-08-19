using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;

namespace IbePerf;

// Load generator: drives one inbound endpoint with N connections (closed- or paced-loop), stamps a
// per-message seq id in MSH-10, records send/ack QPC ticks, and writes latencies.csv + summary.json.
internal static class LoadVerb
{
    private sealed record Rec(long Seq, int Conn, long SendTick, long AckTick, double RttMs, bool Warmup, string Ack, int Bytes);

    private static long _seq;

    public static async Task<int> RunAsync(Cli cli)
    {
        var topo = Topology.Load(cli.Get("contract"));
        var cfg = ScenarioCfg.Load(cli.Get("scenario"));
        var outDir = cli.Get("out");
        Directory.CreateDirectory(outDir);

        var target = ResolveTarget(topo, cfg);
        Console.WriteLine($"[load] scenario '{cfg.Name}' -> {target.Proto} {target.Host}:{target.Port} ({cfg.Mode}, {cfg.Connections} conn, {cfg.DurationSec}s +{cfg.WarmupSec}s warmup)");

        var start = Qpc.Now();
        var warmupEnd = start + (long)(cfg.WarmupSec * Stopwatch_FreqTimes(1));
        var end = start + (long)((cfg.WarmupSec + cfg.DurationSec) * Stopwatch_FreqTimes(1));
        var recs = new ConcurrentQueue<Rec>();

        var mix = BuildMixTable(cfg);
        var workers = new List<Task>();
        for (var c = 0; c < cfg.Connections; c++)
        {
            var conn = c;
            workers.Add(target.Proto switch
            {
                "tcp" => Task.Run(() => TcpWorkerAsync(conn, target, cfg, mix, recs, warmupEnd, end)),
                "ws" => Task.Run(() => WsWorkerAsync(conn, target, cfg, mix, recs, warmupEnd, end)),
                _ => Task.Run(() => HttpWorkerAsync(conn, target, cfg, mix, recs, warmupEnd, end)),
            });
        }
        await Task.WhenAll(workers);

        WriteResults(outDir, cfg, target, recs.ToArray());
        return 0;
    }

    private static double Stopwatch_FreqTimes(int seconds) => System.Diagnostics.Stopwatch.Frequency * (double)seconds;

    private static PerfEndpoint ResolveTarget(Topology topo, ScenarioCfg cfg)
    {
        if (cfg.InputId is int id && topo.InboundById(id) is { } ep) return ep;
        if (topo.Contracts.Count > 0 && topo.Contracts[0].Inputs.Count > 0 && topo.InboundById(topo.Contracts[0].Inputs[0]) is { } c) return c;
        if (topo.Inbound.Count > 0) return topo.Inbound[0];
        throw new InvalidOperationException("no inbound endpoint found in contractData.json");
    }

    private static string[] BuildMixTable(ScenarioCfg cfg)
    {
        // Expand the weighted mix into a 100-slot lookup for O(1) weighted pick.
        var mix = cfg.MessageMix.Count > 0 ? cfg.MessageMix : new List<MessageMix> { new() };
        var table = new List<string>(100);
        foreach (var m in mix)
        {
            var slots = Math.Max(1, (int)Math.Round(m.WeightPct));
            for (var i = 0; i < slots; i++) table.Add($"{m.Type}|{m.SizeBytes}");
        }
        return table.ToArray();
    }

    private static (string Type, int Size) Pick(string[] table, Random rng)
    {
        var entry = table[rng.Next(table.Length)].Split('|');
        return (entry[0], int.Parse(entry[1], CultureInfo.InvariantCulture));
    }

    private static async Task TcpWorkerAsync(int conn, PerfEndpoint ep, ScenarioCfg cfg, string[] mix,
        ConcurrentQueue<Rec> recs, long warmupEnd, long end)
    {
        var rng = new Random(conn * 7919 + 1);
        var perConnInterval = cfg.RateMsgsPerSec > 0 ? Stopwatch_FreqTimes(1) / (cfg.RateMsgsPerSec / cfg.Connections) : 0;
        using var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(ep.Host, ep.Port);
        using var ns = client.GetStream();
        using var reader = new BufferedStream(ns, 8192);
        var sinceBurst = 0;

        while (Qpc.Now() < end)
        {
            var seq = Interlocked.Increment(ref _seq);
            var (type, size) = Pick(mix, rng);
            var frame = Mllp.Frame(Hl7Corpus.Build(type, seq, size));

            var send = Qpc.Now();
            await ns.WriteAsync(frame);
            await ns.FlushAsync();

            var reply = await Mllp.ReadFrameAsync(reader, CancellationToken.None);
            var ack = Qpc.Now();
            var status = reply is null ? "none" : Classify(reply);
            recs.Enqueue(new Rec(seq, conn, send, ack, Qpc.ToMs(ack - send), send < warmupEnd, status, frame.Length));

            if (perConnInterval > 0)
            {
                var spent = Qpc.Now() - send;
                var remainMs = Qpc.ToMs((long)perConnInterval - spent);
                if (remainMs > 1) await Task.Delay((int)remainMs);
            }
            if (cfg.IdleGapSec > 0 && cfg.BurstSize > 0 && ++sinceBurst >= cfg.BurstSize)
            {
                sinceBurst = 0;
                await Task.Delay(cfg.IdleGapSec * 1000);
            }
        }
    }

    private static async Task HttpWorkerAsync(int conn, PerfEndpoint ep, ScenarioCfg cfg, string[] mix,
        ConcurrentQueue<Rec> recs, long warmupEnd, long end)
    {
        var rng = new Random(conn * 7919 + 1);
        var perConnInterval = cfg.RateMsgsPerSec > 0 ? Stopwatch_FreqTimes(1) / (cfg.RateMsgsPerSec / cfg.Connections) : 0;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var url = ep.Url ?? $"http://{ep.Host}:{ep.Port}/";
        var sinceBurst = 0;

        while (Qpc.Now() < end)
        {
            var seq = Interlocked.Increment(ref _seq);
            var (type, size) = Pick(mix, rng);
            var payload = Hl7Corpus.Build(type, seq, size);

            var send = Qpc.Now();
            using var content = new ByteArrayContent(payload);
            var status = "none";
            try
            {
                using var resp = await http.PostAsync(url, content);
                status = resp.IsSuccessStatusCode ? "AA" : "none";
            }
            catch (HttpRequestException) { status = "none"; }
            var ack = Qpc.Now();
            recs.Enqueue(new Rec(seq, conn, send, ack, Qpc.ToMs(ack - send), send < warmupEnd, status, payload.Length));

            if (perConnInterval > 0)
            {
                var remainMs = Qpc.ToMs((long)perConnInterval - (Qpc.Now() - send));
                if (remainMs > 1) await Task.Delay((int)remainMs);
            }
            if (cfg.IdleGapSec > 0 && cfg.BurstSize > 0 && ++sinceBurst >= cfg.BurstSize)
            {
                sinceBurst = 0;
                await Task.Delay(cfg.IdleGapSec * 1000);
            }
        }
    }

    private static string Classify(byte[] reply)
    {
        var text = System.Text.Encoding.UTF8.GetString(reply);
        if (text.Contains("MSA|AA", StringComparison.Ordinal)) return "AA";
        if (text.Contains("MSA|AE", StringComparison.Ordinal) || text.Contains("MSA|AR", StringComparison.Ordinal)) return "AE";
        return "recv";
    }

    // WebSocket: one HL7 payload per binary message; ack is one binary message back on the same socket.
    private static async Task WsWorkerAsync(int conn, PerfEndpoint ep, ScenarioCfg cfg, string[] mix,
        ConcurrentQueue<Rec> recs, long warmupEnd, long end)
    {
        var rng = new Random(conn * 7919 + 1);
        var perConnInterval = cfg.RateMsgsPerSec > 0 ? Stopwatch_FreqTimes(1) / (cfg.RateMsgsPerSec / cfg.Connections) : 0;
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(ep.Url!), CancellationToken.None);
        var recvBuf = new byte[65536];
        var sinceBurst = 0;

        while (Qpc.Now() < end)
        {
            var seq = Interlocked.Increment(ref _seq);
            var (type, size) = Pick(mix, rng);
            var payload = Hl7Corpus.Build(type, seq, size);

            var send = Qpc.Now();
            await socket.SendAsync(payload, WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);
            var reply = await ReceiveWsMessageAsync(socket, recvBuf, CancellationToken.None);
            var ack = Qpc.Now();
            var status = reply is null ? "none" : Classify(reply);
            recs.Enqueue(new Rec(seq, conn, send, ack, Qpc.ToMs(ack - send), send < warmupEnd, status, payload.Length));

            if (perConnInterval > 0)
            {
                var remainMs = Qpc.ToMs((long)perConnInterval - (Qpc.Now() - send));
                if (remainMs > 1) await Task.Delay((int)remainMs);
            }
            if (cfg.IdleGapSec > 0 && cfg.BurstSize > 0 && ++sinceBurst >= cfg.BurstSize)
            {
                sinceBurst = 0;
                await Task.Delay(cfg.IdleGapSec * 1000);
            }
        }
        try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); } catch { /* best effort */ }
    }

    // Accumulates WebSocket fragments into one message; null when the peer closes.
    private static async Task<byte[]?> ReceiveWsMessageAsync(WebSocket socket, byte[] buffer, CancellationToken ct)
    {
        using var acc = new MemoryStream(256);
        while (true)
        {
            var r = await socket.ReceiveAsync(buffer, ct);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            acc.Write(buffer, 0, r.Count);
            if (r.EndOfMessage) return acc.ToArray();
        }
    }

    private static void WriteResults(string outDir, ScenarioCfg cfg, PerfEndpoint target, Rec[] all)
    {
        using (var csv = new StreamWriter(Path.Combine(outDir, "latencies.csv")))
        {
            csv.WriteLine("seq,conn,sendTick,ackTick,rttMs,warmup,ack,bytes");
            foreach (var r in all)
                csv.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{r.Seq},{r.Conn},{r.SendTick},{r.AckTick},{r.RttMs:F4},{(r.Warmup ? 1 : 0)},{r.Ack},{r.Bytes}"));
        }

        var measured = all.Where(r => !r.Warmup).ToArray();
        var rtts = measured.Select(r => r.RttMs).OrderBy(x => x).ToArray();
        var bytes = measured.Sum(r => (long)r.Bytes);
        var ackDist = measured.GroupBy(r => r.Ack).ToDictionary(g => g.Key, g => g.Count());

        var summary = new
        {
            scenario = cfg.Name,
            proto = target.Proto,
            target = $"{target.Host}:{target.Port}",
            mode = cfg.Mode,
            connections = cfg.Connections,
            durationSec = cfg.DurationSec,
            sent = all.Length,
            warmupSent = all.Length - measured.Length,
            measured = measured.Length,
            throughputMsgSec = cfg.DurationSec > 0 ? measured.Length / (double)cfg.DurationSec : 0,
            throughputMbSec = cfg.DurationSec > 0 ? bytes / 1_000_000.0 / cfg.DurationSec : 0,
            rttMs = new
            {
                min = rtts.Length > 0 ? rtts[0] : double.NaN,
                mean = rtts.Length > 0 ? rtts.Average() : double.NaN,
                p50 = Stats.Percentile(rtts, 50),
                p90 = Stats.Percentile(rtts, 90),
                p95 = Stats.Percentile(rtts, 95),
                p99 = Stats.Percentile(rtts, 99),
                p999 = Stats.Percentile(rtts, 99.9),
                max = rtts.Length > 0 ? rtts[^1] : double.NaN,
            },
            ack = ackDist,
        };
        File.WriteAllText(Path.Combine(outDir, "summary.json"),
            JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"[load] '{cfg.Name}': {measured.Length} msgs, {Stats.F(summary.throughputMsgSec)} msg/s, p99 {Stats.F(summary.rttMs.p99)} ms");
    }
}
