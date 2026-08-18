using Microsoft.Extensions.Logging.Abstractions;
using Philips.IBE.IBEAgent.Endpoints.File;
using IoFile = System.IO.File;   // the .File namespace shadows System.IO.File

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class RetentionSweeperTests
{
    [Fact]
    public async Task Sweep_removes_files_older_than_the_retention_window()
    {
        var dir = CreateTempDir();
        try
        {
            var processed = Path.Combine(dir, "processed");
            Directory.CreateDirectory(processed);
            var old = Path.Combine(processed, "old.hl7");
            var fresh = Path.Combine(processed, "fresh.hl7");
            await IoFile.WriteAllTextAsync(old, "o");
            await IoFile.WriteAllTextAsync(fresh, "f");
            IoFile.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-10));

            await new RetentionSweeper(dir, retentionDays: 7, NullLogger.Instance).SweepAsync(CancellationToken.None);

            Assert.False(IoFile.Exists(old));    // 10 days > 7 -> removed
            Assert.True(IoFile.Exists(fresh));   // recent -> kept
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Sweep_also_prunes_the_error_folder()
    {
        var dir = CreateTempDir();
        try
        {
            var error = Path.Combine(dir, "error");
            Directory.CreateDirectory(error);
            var old = Path.Combine(error, "bad.hl7");
            await IoFile.WriteAllTextAsync(old, "x");
            IoFile.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30));

            await new RetentionSweeper(dir, retentionDays: 7, NullLogger.Instance).SweepAsync(CancellationToken.None);

            Assert.False(IoFile.Exists(old));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ibe-ret-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
