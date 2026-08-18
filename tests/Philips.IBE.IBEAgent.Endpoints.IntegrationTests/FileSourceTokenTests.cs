using Microsoft.Extensions.Logging.Abstractions;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Endpoints.File;

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class FileSourceTokenTests
{
    [Fact]
    public async Task CompleteAsync_applies_the_disposition_and_releases_the_guard()
    {
        var dir = CreateTempDir();
        try
        {
            var watermark = new LastProcessedWatermark(dir);
            var disposition = new FileDisposition(FileDispositionMode.Watermark, dir, watermark, NullLogger.Instance);
            var released = false;
            var time = DateTime.UtcNow;
            var token = new FileSourceToken(Path.Combine(dir, "x.hl7"), time, length: 0, payloadHash: "hash", disposition, onCompleted: () => released = true);

            await token.CompleteAsync(MessageCompletion.Completed, CancellationToken.None);

            Assert.True(released);
            Assert.Equal(time, watermark.Read(), TimeSpan.FromMilliseconds(1));   // Watermark mode advanced
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task WriteAsync_is_a_noop_for_file_sources()
    {
        var dir = CreateTempDir();
        try
        {
            var watermark = new LastProcessedWatermark(dir);
            var disposition = new FileDisposition(FileDispositionMode.Watermark, dir, watermark, NullLogger.Instance);
            var token = new FileSourceToken(Path.Combine(dir, "x.hl7"), DateTime.UtcNow, length: 0, payloadHash: "hash", disposition, onCompleted: () => { });

            await token.WriteAsync(new byte[] { 1, 2, 3 }, CancellationToken.None);   // no reply channel

            Assert.Equal(DateTime.MinValue, watermark.Read());   // an ack write must not touch the disposition
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task CompleteAsync_is_idempotent()
    {
        var dir = CreateTempDir();
        try
        {
            var watermark = new LastProcessedWatermark(dir);
            var disposition = new FileDisposition(FileDispositionMode.Watermark, dir, watermark, NullLogger.Instance);
            var releases = 0;
            var token = new FileSourceToken(Path.Combine(dir, "x.hl7"), DateTime.UtcNow, length: 0, payloadHash: "hash", disposition, onCompleted: () => releases++);

            await token.CompleteAsync(MessageCompletion.Completed, CancellationToken.None);
            await token.CompleteAsync(MessageCompletion.Completed, CancellationToken.None);

            Assert.Equal(1, releases);   // settle-once
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
