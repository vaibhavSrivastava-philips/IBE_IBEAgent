using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace IbePerf;

// Downstream simulator: listens on every OUTBOUND endpoint in the topology (TCP/MLLP + HTTP), records
// each received message (seq + QPC receive tick) for loss/dupe/reorder analysis, and returns an ack.
// Supports failure injection (ack delay/jitter, close-after-N, idle-close, NACK%). Runs until the
// --stop file appears, then flushes sink.csv + sink.json.
internal static class SinkVerb
{
    private sealed record Recv(long Seq, long RecvTick, string Proto, long Order);

    public static async Task<int> RunAsync(Cli cli)
    {
        var topo = Topology.Load(cli.Get("contract"));
        var cfg = ScenarioCfg.Load(cli.Get("scenario")).Sink;
        var outDir = cli.Get("out");
        var stopFile = cli.Get("stop");
        var readyFile = cli.Get("ready", "");
        Directory.CreateDirectory(outDir);

        var recs = new ConcurrentQueue<Recv>();
        long order = 0, nacks = 0;
        using var cts = new CancellationTokenSource();
        var tasks = new List<Task>();
        var listeners = new List<TcpListener>();
        var httpListeners = new List<HttpListener>();

        // Only bind outbound endpoints a contract actually delivers to (avoids clashing with unrelated
        // listeners on unused ports); fall back to all outbound if no contract references any.
        var used = topo.Contracts.SelectMany(c => c.Outputs).ToHashSet();
        var targets = topo.Outbound.Where(e => used.Count == 0 || used.Contains(e.Id)).ToList();

        foreach (var ep in targets)
        {
            try
            {
                if (ep.Proto == "tcp")
                {
                    var l = new TcpListener(IPAddress.Loopback, ep.Port);
                    l.Start();
                    listeners.Add(l);
                    tasks.Add(AcceptTcpAsync(l, ep, cfg, recs, () => Interlocked.Increment(ref order), () => Interlocked.Increment(ref nacks), cts.Token));
                    Console.WriteLine($"[sink] TCP listening on {ep.Port} (outputId {ep.Id})");
                }
                else if (ep.Proto == "http" && ep.Url is not null)
                {
                    var prefix = ep.Url.EndsWith('/') ? ep.Url : ep.Url + "/";
                    var h = new HttpListener();
                    h.Prefixes.Add(prefix);
                    h.Start();
                    httpListeners.Add(h);
                    tasks.Add(AcceptHttpAsync(h, ep, cfg, recs, () => Interlocked.Increment(ref order), () => Interlocked.Increment(ref nacks), cts.Token));
                    Console.WriteLine($"[sink] HTTP listening on {prefix} (outputId {ep.Id})");
                }
                else if (ep.Proto == "ws" && ep.Url is not null)
                {
                    var prefix = Topology.ToHttpListenerPrefix(ep.Url);
                    var h = new HttpListener();
                    h.Prefixes.Add(prefix);
                    h.Start();
                    httpListeners.Add(h);
                    tasks.Add(AcceptWsAsync(h, cfg, recs, () => Interlocked.Increment(ref order), () => Interlocked.Increment(ref nacks), cts.Token));
                    Console.WriteLine($"[sink] WS listening on {prefix} (outputId {ep.Id})");
                }
            }
            catch (Exception ex) when (ex is SocketException or HttpListenerException)
            {
                // A single occupied port must not abort the whole sink; warn and carry on.
                Console.Error.WriteLine($"[sink] WARN could not bind outputId {ep.Id} ({ep.Proto} {ep.Port}): {ex.Message}");
            }
        }

        if (!string.IsNullOrEmpty(readyFile)) File.WriteAllText(readyFile, "ready");

        // Poll for the stop signal.
        while (!File.Exists(stopFile)) await Task.Delay(100);
        cts.Cancel();
        foreach (var l in listeners) l.Stop();
        foreach (var h in httpListeners) h.Close();
        try { await Task.WhenAll(tasks); } catch { /* listeners torn down */ }

        WriteResults(outDir, recs, Interlocked.Read(ref nacks));
        Console.WriteLine($"[sink] stopped; {recs.Count} messages received.");
        return 0;
    }

    private static async Task AcceptTcpAsync(TcpListener l, PerfEndpoint ep, SinkCfg cfg, ConcurrentQueue<Recv> recs,
        Func<long> nextOrder, Func<long> onNack, CancellationToken ct)
    {
        var rng = new Random();
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await l.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { break; }
            _ = HandleTcpAsync(client, ep, cfg, recs, nextOrder, onNack, rng, ct);
        }
    }

    private static async Task HandleTcpAsync(TcpClient client, PerfEndpoint ep, SinkCfg cfg, ConcurrentQueue<Recv> recs,
        Func<long> nextOrder, Func<long> onNack, Random rng, CancellationToken ct)
    {
        using (client)
        {
            client.NoDelay = true;
            using var ns = client.GetStream();
            using var reader = new BufferedStream(ns, 8192);
            var handled = 0;
            while (!ct.IsCancellationRequested)
            {
                byte[]? frame;
                if (cfg.Failure.IdleCloseMs > 0)
                {
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    idle.CancelAfter(cfg.Failure.IdleCloseMs);
                    try { frame = await Mllp.ReadFrameAsync(reader, idle.Token); }
                    catch (OperationCanceledException) { break; } // idle timeout -> close (simulate downstream)
                }
                else
                {
                    frame = await Mllp.ReadFrameAsync(reader, ct);
                }
                if (frame is null) break;

                var seq = Hl7Corpus.ExtractSeq(frame);
                recs.Enqueue(new Recv(seq, Qpc.Now(), "tcp", nextOrder()));

                if (cfg.AckDelayMs > 0) await Task.Delay(cfg.AckDelayMs + rng.Next(cfg.JitterMs + 1), ct);
                if (cfg.Failure.CloseAfterN > 0 && ++handled >= cfg.Failure.CloseAfterN) break;

                var nack = cfg.Failure.NackPct > 0 && rng.NextDouble() * 100 < cfg.Failure.NackPct;
                if (nack) onNack();
                await ns.WriteAsync(Mllp.Frame(Hl7Corpus.Msa(nack ? "AE" : "AA", seq)), ct);
                await ns.FlushAsync(ct);
            }
        }
    }

    private static async Task AcceptHttpAsync(HttpListener h, PerfEndpoint ep, SinkCfg cfg, ConcurrentQueue<Recv> recs,
        Func<long> nextOrder, Func<long> onNack, CancellationToken ct)
    {
        var rng = new Random();
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await h.GetContextAsync(); }
            catch { break; }
            _ = HandleHttpAsync(ctx, cfg, recs, nextOrder, onNack, rng, ct);
        }
    }

    private static async Task HandleHttpAsync(HttpListenerContext ctx, SinkCfg cfg, ConcurrentQueue<Recv> recs,
        Func<long> nextOrder, Func<long> onNack, Random rng, CancellationToken ct)
    {
        using var body = new MemoryStream();
        await ctx.Request.InputStream.CopyToAsync(body, ct);
        var seq = Hl7Corpus.ExtractSeq(body.ToArray());
        recs.Enqueue(new Recv(seq, Qpc.Now(), "http", nextOrder()));

        if (cfg.AckDelayMs > 0) await Task.Delay(cfg.AckDelayMs + rng.Next(cfg.JitterMs + 1), ct);
        var nack = cfg.Failure.NackPct > 0 && rng.NextDouble() * 100 < cfg.Failure.NackPct;
        if (nack) onNack();

        var bytes = Hl7Corpus.Msa(nack ? "AE" : "AA", seq);
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes, ct);
        ctx.Response.Close();
    }

    private static async Task AcceptWsAsync(HttpListener h, SinkCfg cfg, ConcurrentQueue<Recv> recs,
        Func<long> nextOrder, Func<long> onNack, CancellationToken ct)
    {
        var rng = new Random();
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await h.GetContextAsync(); }
            catch { break; }
            if (!ctx.Request.IsWebSocketRequest) { ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }
            _ = HandleWsAsync(ctx, cfg, recs, nextOrder, onNack, rng, ct);
        }
    }

    private static async Task HandleWsAsync(HttpListenerContext ctx, SinkCfg cfg, ConcurrentQueue<Recv> recs,
        Func<long> nextOrder, Func<long> onNack, Random rng, CancellationToken ct)
    {
        HttpListenerWebSocketContext wsCtx;
        try { wsCtx = await ctx.AcceptWebSocketAsync(subProtocol: null); }
        catch { ctx.Response.StatusCode = 500; ctx.Response.Close(); return; }
        using var socket = wsCtx.WebSocket;
        var buffer = new byte[65536];
        var handled = 0;
        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                byte[]? payload;
                if (cfg.Failure.IdleCloseMs > 0)
                {
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    idle.CancelAfter(cfg.Failure.IdleCloseMs);
                    try { payload = await ReceiveWsAsync(socket, buffer, idle.Token); }
                    catch (OperationCanceledException) { break; }   // idle timeout -> close (simulate downstream)
                }
                else
                {
                    payload = await ReceiveWsAsync(socket, buffer, ct);
                }
                if (payload is null) break;

                var seq = Hl7Corpus.ExtractSeq(payload);
                recs.Enqueue(new Recv(seq, Qpc.Now(), "ws", nextOrder()));

                if (cfg.AckDelayMs > 0) await Task.Delay(cfg.AckDelayMs + rng.Next(cfg.JitterMs + 1), ct);
                if (cfg.Failure.CloseAfterN > 0 && ++handled >= cfg.Failure.CloseAfterN) break;
                var nack = cfg.Failure.NackPct > 0 && rng.NextDouble() * 100 < cfg.Failure.NackPct;
                if (nack) onNack();
                await socket.SendAsync(Hl7Corpus.Msa(nack ? "AE" : "AA", seq), WebSocketMessageType.Binary, endOfMessage: true, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }

    private static async Task<byte[]?> ReceiveWsAsync(System.Net.WebSockets.WebSocket socket, byte[] buffer, CancellationToken ct)
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

    private static void WriteResults(string outDir, ConcurrentQueue<Recv> recs, long nacks)
    {
        var all = recs.ToArray();
        var seen = new HashSet<long>();
        long duplicates = 0, outOfOrder = 0, lastSeq = -1;
        var tcp = 0; var http = 0; var ws = 0;
        using (var csv = new StreamWriter(Path.Combine(outDir, "sink.csv")))
        {
            csv.WriteLine("seq,recvTick,proto,order");
            foreach (var r in all.OrderBy(r => r.Order))
            {
                csv.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{r.Seq},{r.RecvTick},{r.Proto},{r.Order}"));
                if (!seen.Add(r.Seq)) duplicates++;
                if (r.Seq >= 0 && r.Seq < lastSeq) outOfOrder++;
                lastSeq = r.Seq;
                if (r.Proto == "tcp") tcp++;
                else if (r.Proto == "ws") ws++;
                else http++;
            }
        }

        var summary = new
        {
            receivedTcp = tcp,
            receivedHttp = http,
            receivedWs = ws,
            total = all.Length,
            distinct = seen.Count,
            duplicates,
            outOfOrder,
            nacksSent = nacks,
        };
        File.WriteAllText(Path.Combine(outDir, "sink.json"),
            JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    }
}
