using Microsoft.Extensions.Logging;
using IoFile = System.IO.File;   // the enclosing namespace ends in ".File", which shadows System.IO.File

namespace Philips.IBE.IBEAgent.Endpoints.File;

// Deletes disposed files (under processed/ and error/) older than the retention window. Best-effort:
// per-file errors are swallowed so one locked file does not stop the sweep. Retention only applies to
// the Move disposition (which is what populates those folders).
public sealed class RetentionSweeper
{
    private readonly string[] _folders;
    private readonly TimeSpan _retention;
    private readonly ILogger _logger;

    public RetentionSweeper(string rootDirectory, int retentionDays, ILogger logger)
    {
        var root = Path.GetFullPath(rootDirectory);
        _folders = [Path.Combine(root, FileDisposition.ProcessedFolder), Path.Combine(root, FileDisposition.ErrorFolder)];
        _retention = TimeSpan.FromDays(Math.Max(0, retentionDays));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task SweepAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - _retention;
        var removed = 0;
        foreach (var folder in _folders)
        {
            if (!Directory.Exists(folder)) continue;
            List<string> files;
            try { files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToList(); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested) break;
                try
                {
                    if (IoFile.GetLastWriteTimeUtc(file) < cutoff) { IoFile.Delete(file); removed++; }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        if (removed > 0)
            _logger.LogInformation("Retention removed {Count} expired file(s).", removed);
        return Task.CompletedTask;
    }
}
