using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Philips.IBE.IBEAgent.Abstractions;
using IoFile = System.IO.File;   // the enclosing namespace ends in ".File", which shadows System.IO.File

namespace Philips.IBE.IBEAgent.Endpoints.File;

// §3.1 — File INPUT: polls a folder (via a pluggable IFileArrivalTrigger), reads each new file into a
// MessageContext, and dispatches it. Source is NoAck (Phase 2); a .lastProcessedTime watermark
// (advanced when a message settles, via FileSourceToken) is the consume-marker so files are not re-read.
public sealed class FileInboundEndpoint : IInboundEndpoint
{
    private readonly FileInboundOptions _options;
    private readonly IMessageDispatcher _dispatcher;
    private readonly IReplyContextFactory _replyFactory;
    private readonly IFileArrivalTrigger _trigger;
    private readonly LastProcessedWatermark _watermark;
    private readonly ProcessedFileJournal? _processedJournal;
    private readonly FileDispositionMode _dispositionMode;
    private readonly FileDisposition _disposition;
    private readonly RetentionSweeper? _retention;
    private readonly ILogger<FileInboundEndpoint> _logger;
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly string[] _extensions;
    private readonly FileShareCredential? _credential;
    private NetworkShareConnection? _share;
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);
    private DateTime _lastSweepUtc = DateTime.MinValue;

    public FileInboundEndpoint(
        FileInboundOptions options,
        IMessageDispatcher dispatcher,
        IReplyContextFactory replyFactory,
        IFileArrivalTrigger? trigger = null,
        ILogger<FileInboundEndpoint>? logger = null,
        FileShareCredential? credential = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _replyFactory = replyFactory ?? throw new ArgumentNullException(nameof(replyFactory));
        _trigger = trigger ?? new PollingFileTrigger(TimeSpan.FromSeconds(Math.Max(1, options.PollIntervalSeconds)));
        _watermark = new LastProcessedWatermark(options.Directory);
        _logger = logger ?? NullLogger<FileInboundEndpoint>.Instance;
        _extensions = ParseExtensions(options.FilePattern);
        _dispositionMode = ResolveDisposition(options);
        _processedJournal = _dispositionMode == FileDispositionMode.Watermark ? new ProcessedFileJournal(options.Directory) : null;
        _disposition = new FileDisposition(_dispositionMode, options.Directory, _watermark, _logger, _processedJournal);
        _retention = options.RetentionDays > 0 ? new RetentionSweeper(options.Directory, options.RetentionDays, _logger) : null;
        _credential = credential;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ConnectShareIfConfigured();
        Directory.CreateDirectory(_options.Directory);
        // Watermark mode: on first start, arm the marker to "now" so a pre-existing backlog is not ingested
        // (an operator-set marker is preserved). Legacy KeepOriginalFiles parity, without the manual arming step.
        if (_dispositionMode == FileDispositionMode.Watermark)
            await _watermark.ArmAsync(DateTime.UtcNow, cancellationToken);
        _logger.LogInformation(
            "File inbound endpoint (source {SourceEndpointId}) polling {Directory}.",
            _options.SourceEndpointId, _options.Directory);
        await _trigger.StartAsync(SafeScanAsync, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _trigger.StopAsync(cancellationToken);
        DisposeShare();
        _logger.LogInformation("File inbound endpoint (source {SourceEndpointId}) stopped.", _options.SourceEndpointId);
    }

    private async Task SafeScanAsync(CancellationToken cancellationToken)
    {
        try { await ScanOnceAsync(cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File inbound scan failed for source {SourceEndpointId}.", _options.SourceEndpointId);
        }
    }

    // Public for direct testability (one scan without the poll loop), mirroring ForwardWorker.RunOneSweepAsync.
    public async Task ScanOnceAsync(CancellationToken cancellationToken)
    {
        EnsureShareConnected();
        var since = _watermark.Read();
        foreach (var (path, timeUtc, length) in EnumerateEligible(since))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_inFlight.TryAdd(path, 0)) continue;   // dispatched by an earlier scan, not yet settled

            var dispatched = false;
            try
            {
                byte[] payload;
                try { payload = await IoFile.ReadAllBytesAsync(path, cancellationToken); }
                catch (IOException ex)                    // locked / vanished -> retry next poll
                {
                    _logger.LogDebug(ex, "Could not read {File} yet; will retry.", path);
                    continue;
                }

                if (_processedJournal is not null && await _processedJournal.ContainsAsync(path, length, payload, cancellationToken))
                    continue;

                var payloadHash = ProcessedFileJournal.ComputePayloadHash(payload);

                var token = new FileSourceToken(path, timeUtc, length, payloadHash, _disposition, () => _inFlight.TryRemove(path, out _));
                var reply = _replyFactory.Create(_options.SourceEndpointId, token);
                var ctx = new MessageContext(
                    correlationId: Guid.NewGuid().ToString("N"),
                    sourceEndpointId: _options.SourceEndpointId,
                    format: _options.Format,
                    ack: token,
                    reply: reply,
                    payload: payload,
                    headers: BuildSourceHeaders(path),
                    disposition: token);

                _logger.LogDebug(
                    "Received message {CorrelationId} ({ByteCount} bytes) from file source {SourceEndpointId}.",
                    ctx.CorrelationId, payload.Length, _options.SourceEndpointId);

                ctx.Reply.Attach(ctx);
                await _dispatcher.DispatchAsync(ctx, cancellationToken);   // backpressure from the ingress queue
                dispatched = true;   // the token's disposition now owns releasing the in-flight guard (at settle)
            }
            finally
            {
                if (!dispatched) _inFlight.TryRemove(path, out _);
            }
        }

        await MaybeSweepAsync(cancellationToken);
    }

    // Retention runs at most once per SweepInterval, piggy-backing on the poll loop (no extra timer).
    private async Task MaybeSweepAsync(CancellationToken cancellationToken)
    {
        if (_retention is null) return;
        var now = DateTime.UtcNow;
        if (now - _lastSweepUtc < SweepInterval) return;
        _lastSweepUtc = now;
        await _retention.SweepAsync(cancellationToken);
    }

    private void ConnectShareIfConfigured()
    {
        if (_credential is null || !UncPath.IsUnc(_options.Directory)) return;
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("File-share credentials are only supported on Windows.");
        // WNetAddConnection2 needs the backslash UNC form; config may use forward slashes (legacy parity).
        _share = new NetworkShareConnection(UncPath.ToRemoteName(_options.Directory), _credential, _logger);
        _share.EnsureConnected();
    }

    private void EnsureShareConnected()
    {
        if (_share is null || !OperatingSystem.IsWindows()) return;
        try { _share.EnsureConnected(); }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Network share {Directory} reconnect failed; will retry next poll.", _options.Directory);
        }
    }

    private void DisposeShare()
    {
        if (_share is null || !OperatingSystem.IsWindows()) return;
        _share.Dispose();
        _share = null;
    }

    private List<(string Path, DateTime TimeUtc, long Length)> EnumerateEligible(DateTime since)
    {
        var searchOption = _options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        List<string> files;
        try { files = Directory.EnumerateFiles(_options.Directory, "*", searchOption).ToList(); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }

        var eligible = new List<(string Path, DateTime TimeUtc, long Length)>();
        foreach (var path in files)
        {
            if (IsInternal(path) || !MatchesExtension(path)) continue;
            var timeUtc = EffectiveTimeUtc(path);
            var length = FileLength(path);
            if (timeUtc > since) eligible.Add((path, timeUtc, length));
        }
        eligible.Sort(static (a, b) => a.TimeUtc.CompareTo(b.TimeUtc));   // oldest first
        return eligible;
    }

    private static string[] ParseExtensions(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return [];
        return pattern
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.TrimStart('*').TrimStart('.').ToLowerInvariant())
            .Where(p => p.Length > 0)
            .ToArray();
    }

    // Legacy-parity source provenance, opted into wire forwarding: a header-capable output (HTTP)
    // relays the file name (bare) + path (full) to the downstream; TCP/File outputs ignore them.
    private static Dictionary<string, string> BuildSourceHeaders(string path) => new(StringComparer.Ordinal)
    {
        [ForwardHeaders.Key("filesourcepath")] = Path.GetFileName(path),
        [ForwardHeaders.Key("FilePath")] = path,
    };

    // KeepOriginalFiles selects how a consumed file is retired: true -> Watermark (keep the file + advance
    // the .lastProcessedTime marker), false -> Move (relocate to processed/ or error/).
    private static FileDispositionMode ResolveDisposition(FileInboundOptions options) =>
        options.KeepOriginalFiles ? FileDispositionMode.Watermark : FileDispositionMode.Move;

    private bool MatchesExtension(string path)
    {
        if (_extensions.Length == 0) return true;
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return Array.IndexOf(_extensions, ext) >= 0;
    }

    private static bool IsInternal(string path)
    {
        if (Path.GetFileName(path).StartsWith(LastProcessedWatermark.FileName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (Path.GetFileName(path).StartsWith(ProcessedFileJournal.FileName, StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Equals("processed", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("error", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static DateTime EffectiveTimeUtc(string path)
    {
        try
        {
            var write = IoFile.GetLastWriteTimeUtc(path);
            var create = IoFile.GetCreationTimeUtc(path);
            return write >= create ? write : create;
        }
        catch (IOException) { return DateTime.MinValue; }
        catch (UnauthorizedAccessException) { return DateTime.MinValue; }
    }

    private static long FileLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (IOException) { return -1; }
        catch (UnauthorizedAccessException) { return -1; }
    }
}
