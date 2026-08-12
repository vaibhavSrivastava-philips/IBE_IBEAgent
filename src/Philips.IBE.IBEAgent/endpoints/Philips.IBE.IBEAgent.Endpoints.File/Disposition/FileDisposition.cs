using Microsoft.Extensions.Logging;
using Philips.IBE.IBEAgent.Abstractions;
using IoFile = System.IO.File;   // the enclosing namespace ends in ".File", which shadows System.IO.File

namespace Philips.IBE.IBEAgent.Endpoints.File;

public enum FileDispositionMode { Move, Watermark }

// How a consumed source file is retired when its message settles. Move (default, legacy parity)
// relocates the file to processed/ (Completed/Filtered) or error/ (Faulted), preserving its relative
// path under the polled root; Watermark leaves the file and advances .lastProcessedTime (read-only
// shares). Best-effort: a failed disposition is logged, never thrown.
public sealed class FileDisposition
{
    public const string ProcessedFolder = "processed";
    public const string ErrorFolder = "error";

    private readonly FileDispositionMode _mode;
    private readonly string _root;
    private readonly LastProcessedWatermark _watermark;
    private readonly ProcessedFileJournal? _processedJournal;
    private readonly ILogger _logger;

    public FileDisposition(FileDispositionMode mode, string rootDirectory, LastProcessedWatermark watermark, ILogger logger, ProcessedFileJournal? processedJournal = null)
    {
        _mode = mode;
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        _watermark = watermark ?? throw new ArgumentNullException(nameof(watermark));
        _processedJournal = processedJournal;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ApplyAsync(string sourcePath, DateTime effectiveTimeUtc, long length, string payloadHash, MessageCompletion outcome, CancellationToken cancellationToken)
    {
        try
        {
            switch (_mode)
            {
                case FileDispositionMode.Watermark:
                    await _watermark.AdvanceToAsync(effectiveTimeUtc, cancellationToken);
                    if (outcome != MessageCompletion.Faulted && _processedJournal is not null)
                        await _processedJournal.AddAsync(sourcePath, length, payloadHash, cancellationToken);
                    break;
                default:
                    MoveToOutcomeFolder(sourcePath, outcome);
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex,
                "File disposition ({Mode}) failed for {File}; the source may be reprocessed.", _mode, sourcePath);
        }
    }

    private void MoveToOutcomeFolder(string sourcePath, MessageCompletion outcome)
    {
        if (!IoFile.Exists(sourcePath)) return;
        var subfolder = outcome == MessageCompletion.Faulted ? ErrorFolder : ProcessedFolder;
        var relative = Path.GetRelativePath(_root, Path.GetFullPath(sourcePath));
        var destination = Path.Combine(_root, subfolder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        IoFile.Move(sourcePath, destination, overwrite: true);
    }
}
