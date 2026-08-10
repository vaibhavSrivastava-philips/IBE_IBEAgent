using Philips.IBE.IBEAgent.Endpoints.File;
using IoFile = System.IO.File;   // the .File namespace shadows System.IO.File

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class LastProcessedWatermarkTests
{
    [Fact]
    public async Task Arm_creates_a_hidden_marker_at_the_given_time_when_missing()
    {
        var dir = CreateTempDir();
        try
        {
            var watermark = new LastProcessedWatermark(dir);
            Assert.False(watermark.Exists());

            var armAt = new DateTime(2026, 8, 8, 10, 0, 0, DateTimeKind.Utc);
            await watermark.ArmAsync(armAt, CancellationToken.None);

            Assert.True(watermark.Exists());
            Assert.Equal(armAt, watermark.Read());
            if (OperatingSystem.IsWindows())
            {
                var attrs = IoFile.GetAttributes(Path.Combine(dir, LastProcessedWatermark.FileName));
                Assert.True(attrs.HasFlag(FileAttributes.Hidden));   // legacy parity: the marker is hidden
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Arm_does_not_overwrite_an_operator_set_marker()
    {
        var dir = CreateTempDir();
        try
        {
            var watermark = new LastProcessedWatermark(dir);
            var operatorStart = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await watermark.AdvanceToAsync(operatorStart, CancellationToken.None);   // a chosen start point

            await watermark.ArmAsync(DateTime.UtcNow, CancellationToken.None);       // must NOT clobber it

            Assert.Equal(operatorStart, watermark.Read());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ibe-wm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
