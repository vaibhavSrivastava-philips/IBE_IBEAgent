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
            var tmp = _path + ".tmp";
            await IoFile.WriteAllTextAsync(tmp, candidateUtc.ToString("O", CultureInfo.InvariantCulture), cancellationToken);
            IoFile.Move(tmp, _path, overwrite: true);
        }
        finally { _gate.Release(); }
    }
}
