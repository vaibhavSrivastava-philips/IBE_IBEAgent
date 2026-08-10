using System.Globalization;
using IoFile = System.IO.File;   // the enclosing namespace ends in ".File", which shadows System.IO.File

namespace Philips.IBE.IBEAgent.Endpoints.File;

// The ".lastProcessedTime" consume-marker for a polled folder: files with an effective time at or
// before this are not re-read. Advanced monotonically when a message settles. Writes are serialized
// and atomic (temp -> move).
public sealed class LastProcessedWatermark
{
    public const string FileName = ".lastProcessedTime";
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LastProcessedWatermark(string directory) => _path = Path.Combine(directory, FileName);

    public bool Exists() => IoFile.Exists(_path);

    public DateTime Read()
    {
        try
        {
            if (!IoFile.Exists(_path)) return DateTime.MinValue;
            var text = IoFile.ReadAllText(_path).Trim();
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)
                ? t.ToUniversalTime()
                : DateTime.MinValue;
        }
        catch (IOException) { return DateTime.MinValue; }
        catch (UnauthorizedAccessException) { return DateTime.MinValue; }
    }

    public async Task AdvanceToAsync(DateTime candidateUtc, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (candidateUtc <= Read()) return;   // monotonic: never move backwards
            await WriteAsync(candidateUtc, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    // First-run arming: create the marker at 'utc' only when it is missing, so a pre-existing backlog is
    // skipped while an operator-set marker (a chosen start point) is preserved.
    public async Task ArmAsync(DateTime utc, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!IoFile.Exists(_path)) await WriteAsync(utc, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task WriteAsync(DateTime utc, CancellationToken cancellationToken)
    {
        var tmp = _path + ".tmp";
        await IoFile.WriteAllTextAsync(tmp, utc.ToString("O", CultureInfo.InvariantCulture), cancellationToken);
        ClearHidden(_path);   // Move(overwrite) onto a hidden target can fail on Windows; clear then re-hide
        IoFile.Move(tmp, _path, overwrite: true);
        SetHidden(_path);     // keep the marker out of the way (legacy parity); best-effort
    }

    private static void ClearHidden(string path)
    {
        try
        {
            if (IoFile.Exists(path))
                IoFile.SetAttributes(path, IoFile.GetAttributes(path) & ~FileAttributes.Hidden);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (PlatformNotSupportedException) { }
    }

    private static void SetHidden(string path)
    {
        try { IoFile.SetAttributes(path, IoFile.GetAttributes(path) | FileAttributes.Hidden); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (PlatformNotSupportedException) { }
    }
}
