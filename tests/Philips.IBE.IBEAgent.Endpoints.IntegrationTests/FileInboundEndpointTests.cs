using System.Text;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Endpoints.File;
using Philips.IBE.IBEAgent.TestKit;
using IoFile = System.IO.File;   // the .File namespace shadows System.IO.File

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class FileInboundEndpointTests
{
    [Fact]
    public async Task Scan_reads_and_dispatches_a_new_file()
    {
        var dir = CreateTempDir();
        try
        {
            await IoFile.WriteAllTextAsync(Path.Combine(dir, "a.hl7"), "MSH|payload");
            var dispatcher = new FakeMessageDispatcher();
            var endpoint = NewEndpoint(dir, dispatcher);

            await endpoint.ScanOnceAsync(CancellationToken.None);

            Assert.Single(dispatcher.Dispatched);
            Assert.Equal("MSH|payload", Encoding.UTF8.GetString(dispatcher.Dispatched[0].Payload.Span));
            Assert.Equal(7, dispatcher.Dispatched[0].SourceEndpointId);
            Assert.NotNull(dispatcher.Dispatched[0].Disposition);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Settled_file_is_not_reprocessed_on_the_next_scan()
    {
        var dir = CreateTempDir();
        try
        {
            await IoFile.WriteAllTextAsync(Path.Combine(dir, "a.hl7"), "one");
            var dispatcher = new FakeMessageDispatcher();
            var endpoint = NewEndpoint(dir, dispatcher);

            await endpoint.ScanOnceAsync(CancellationToken.None);
            Assert.Single(dispatcher.Dispatched);

            // settle the message -> advances the watermark + releases the in-flight guard
            await dispatcher.Dispatched[0].Disposition!.CompleteAsync(MessageCompletion.Completed, CancellationToken.None);

            await endpoint.ScanOnceAsync(CancellationToken.None);
            Assert.Single(dispatcher.Dispatched);   // still 1 — not re-read
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task In_flight_file_is_not_dispatched_twice_before_it_settles()
    {
        var dir = CreateTempDir();
        try
        {
            await IoFile.WriteAllTextAsync(Path.Combine(dir, "a.hl7"), "one");
            var dispatcher = new FakeMessageDispatcher();
            var endpoint = NewEndpoint(dir, dispatcher);

            await endpoint.ScanOnceAsync(CancellationToken.None);
            await endpoint.ScanOnceAsync(CancellationToken.None);   // not settled yet -> in-flight guard blocks

            Assert.Single(dispatcher.Dispatched);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Extension_filter_skips_non_matching_files()
    {
        var dir = CreateTempDir();
        try
        {
            await IoFile.WriteAllTextAsync(Path.Combine(dir, "keep.hl7"), "yes");
            await IoFile.WriteAllTextAsync(Path.Combine(dir, "skip.txt"), "no");
            var dispatcher = new FakeMessageDispatcher();
            var endpoint = NewEndpoint(dir, dispatcher, pattern: "*.hl7");

            await endpoint.ScanOnceAsync(CancellationToken.None);

            Assert.Single(dispatcher.Dispatched);
            Assert.Equal("yes", Encoding.UTF8.GetString(dispatcher.Dispatched[0].Payload.Span));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Credential_is_ignored_for_a_local_non_unc_path()
    {
        var dir = CreateTempDir();
        try
        {
            await IoFile.WriteAllTextAsync(Path.Combine(dir, "a.hl7"), "local");
            var dispatcher = new FakeMessageDispatcher();
            var endpoint = new FileInboundEndpoint(
                new FileInboundOptions { SourceEndpointId = 7, Directory = dir },
                dispatcher,
                new FakeReplyContextFactory(),
                credential: new FileShareCredential("user", "DOMAIN", "secret"));   // ignored: dir is local, not UNC

            await endpoint.ScanOnceAsync(CancellationToken.None);

            Assert.Single(dispatcher.Dispatched);
            Assert.Equal("local", Encoding.UTF8.GetString(dispatcher.Dispatched[0].Payload.Span));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Sets_forwardable_source_file_headers()
    {
        var dir = CreateTempDir();
        try
        {
            await IoFile.WriteAllTextAsync(Path.Combine(dir, "a.hl7"), "MSH|x");
            var dispatcher = new FakeMessageDispatcher();
            var endpoint = NewEndpoint(dir, dispatcher);

            await endpoint.ScanOnceAsync(CancellationToken.None);

            var headers = dispatcher.Dispatched[0].Headers;
            Assert.Equal("a.hl7", headers[ForwardHeaders.Key("filesourcepath")]);   // bare name (legacy wire header)
            Assert.EndsWith("a.hl7", headers[ForwardHeaders.Key("FilePath")]);       // full source path
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Watermark_mode_arms_on_start_and_skips_the_pre_existing_backlog()
    {
        var dir = CreateTempDir();
        try
        {
            var backlog = Path.Combine(dir, "old.hl7");
            await IoFile.WriteAllTextAsync(backlog, "OLD");
            IoFile.SetLastWriteTimeUtc(backlog, DateTime.UtcNow.AddMinutes(-5));   // clearly before the arm point

            var dispatcher = new FakeMessageDispatcher();
            var endpoint = new FileInboundEndpoint(
                new FileInboundOptions { SourceEndpointId = 7, Directory = dir, KeepOriginalFiles = true },
                dispatcher, new FakeReplyContextFactory(), new NoOpTrigger());

            await endpoint.StartAsync(CancellationToken.None);   // arms .lastProcessedTime to ~now
            await endpoint.ScanOnceAsync(CancellationToken.None);
            Assert.Empty(dispatcher.Dispatched);                 // backlog older than the arm point is skipped
            Assert.True(IoFile.Exists(Path.Combine(dir, LastProcessedWatermark.FileName)));

            var fresh = Path.Combine(dir, "new.hl7");
            await IoFile.WriteAllTextAsync(fresh, "NEW");
            IoFile.SetLastWriteTimeUtc(fresh, DateTime.UtcNow.AddMinutes(5));       // clearly after the arm point
            await endpoint.ScanOnceAsync(CancellationToken.None);
            Assert.Single(dispatcher.Dispatched);
            Assert.Equal("NEW", Encoding.UTF8.GetString(dispatcher.Dispatched[0].Payload.Span));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static FileInboundEndpoint NewEndpoint(string dir, FakeMessageDispatcher dispatcher, string? pattern = null)
        => new(
            new FileInboundOptions { SourceEndpointId = 7, Directory = dir, FilePattern = pattern },
            dispatcher,
            new FakeReplyContextFactory());

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ibe-filein-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // Captures the tick callback but never loops, so a test can drive ScanOnceAsync manually after StartAsync.
    private sealed class NoOpTrigger : IFileArrivalTrigger
    {
        public Task StartAsync(Func<CancellationToken, Task> onTick, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
