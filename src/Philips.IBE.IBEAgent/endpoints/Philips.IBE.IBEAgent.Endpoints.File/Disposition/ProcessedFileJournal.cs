using System.Globalization;
using System.Security.Cryptography;
using IoFile = System.IO.File;

namespace Philips.IBE.IBEAgent.Endpoints.File;

public sealed class ProcessedFileJournal
{
    public const string FileName = ".processedFiles";
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _entries = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public ProcessedFileJournal(string directory)
    {
        _path = Path.Combine(directory, FileName);
    }

    public async Task<bool> ContainsAsync(string filePath, long length, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _entries.Contains(Key(filePath, length, payload.Span));
    }

    public async Task AddAsync(string filePath, long length, string payloadHash, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await LoadIfNeededAsync(cancellationToken);
            var key = Key(filePath, length, payloadHash);
            if (!_entries.Add(key))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await IoFile.AppendAllTextAsync(_path, key + Environment.NewLine, cancellationToken);
            SetHidden(_path);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded) return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await LoadIfNeededAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task LoadIfNeededAsync(CancellationToken cancellationToken)
    {
        if (_loaded) return;
        if (IoFile.Exists(_path))
        {
            var lines = await IoFile.ReadAllLinesAsync(_path, cancellationToken);
            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    _entries.Add(line.Trim());
            }
        }
        _loaded = true;
    }

    public static string ComputePayloadHash(ReadOnlySpan<byte> payload)
        => Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

    private static string Key(string filePath, long length, ReadOnlySpan<byte> payload)
        => Key(filePath, length, ComputePayloadHash(payload));

    private static string Key(string filePath, long length, string payloadHash)
        => string.Join('|', Path.GetFullPath(filePath), length.ToString(CultureInfo.InvariantCulture), payloadHash);

    private static void SetHidden(string path)
    {
        try { IoFile.SetAttributes(path, IoFile.GetAttributes(path) | FileAttributes.Hidden); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (PlatformNotSupportedException) { }
    }
}
