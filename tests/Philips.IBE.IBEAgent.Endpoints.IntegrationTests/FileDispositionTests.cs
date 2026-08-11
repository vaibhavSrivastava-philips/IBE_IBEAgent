using Microsoft.Extensions.Logging.Abstractions;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Endpoints.File;
using IoFile = System.IO.File;   // the .File namespace shadows System.IO.File

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class FileDispositionTests
{
    [Fact]
    public async Task Move_relocates_a_completed_file_to_processed()
    {
        var dir = CreateTempDir();
        try
        {
            var src = Path.Combine(dir, "a.hl7");
            await IoFile.WriteAllTextAsync(src, "x");

            await NewMove(dir).ApplyAsync(src, DateTime.UtcNow, MessageCompletion.Completed, CancellationToken.None);

            Assert.False(IoFile.Exists(src));
            Assert.True(IoFile.Exists(Path.Combine(dir, "processed", "a.hl7")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Move_relocates_a_faulted_file_to_error()
    {
        var dir = CreateTempDir();
        try
        {
            var src = Path.Combine(dir, "a.hl7");
            await IoFile.WriteAllTextAsync(src, "x");

            await NewMove(dir).ApplyAsync(src, DateTime.UtcNow, MessageCompletion.Faulted, CancellationToken.None);

            Assert.False(IoFile.Exists(src));
            Assert.True(IoFile.Exists(Path.Combine(dir, "error", "a.hl7")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Move_preserves_the_relative_path_for_recursive_inputs()
    {
        var dir = CreateTempDir();
        try
        {
            var sub = Path.Combine(dir, "unitA");
            Directory.CreateDirectory(sub);
            var src = Path.Combine(sub, "a.hl7");
            await IoFile.WriteAllTextAsync(src, "x");

            await NewMove(dir).ApplyAsync(src, DateTime.UtcNow, MessageCompletion.Completed, CancellationToken.None);

            Assert.True(IoFile.Exists(Path.Combine(dir, "processed", "unitA", "a.hl7")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static FileDisposition NewMove(string dir)
        => new(FileDispositionMode.Move, dir, new LastProcessedWatermark(dir), NullLogger.Instance);

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ibe-disp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
