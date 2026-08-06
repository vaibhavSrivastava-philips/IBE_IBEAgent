using System.Text;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.Endpoints.File;
using Philips.IBE.IBEAgent.TestKit;
using IoFile = System.IO.File;   // the .File namespace shadows System.IO.File

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class FileOutboundEndpointTests
{
    private static MessageContext Message(string correlationId, string payload)
        => new(correlationId, sourceEndpointId: 1, format: MessageFormats.Hl7v2,
               ack: new FakeAckToken(), reply: new RecordingReplyContext(),
               payload: Encoding.UTF8.GetBytes(payload));

    [Fact]
    public async Task Writes_payload_to_a_file_in_the_target_directory()
    {
        var dir = CreateTempDir();
        try
        {
            var endpoint = new FileOutboundEndpoint(
                new FileOutboundOptions { Directory = dir, FileNameTemplate = "{correlationId}.txt" }, codec: null);

            var result = await endpoint.SendAsync(Message("cid-1", "HELLO"), CancellationToken.None);

            Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
            var written = Path.Combine(dir, "cid-1.txt");
            Assert.True(IoFile.Exists(written));
            Assert.Equal("HELLO", await IoFile.ReadAllTextAsync(written));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));   // temp cleaned up
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Creates_the_directory_when_missing()
    {
        var baseDir = CreateTempDir();
        try
        {
            var dir = Path.Combine(baseDir, "nested", "out");
            var endpoint = new FileOutboundEndpoint(
                new FileOutboundOptions { Directory = dir, FileNameTemplate = "m.txt" }, codec: null);

            var result = await endpoint.SendAsync(Message("c", "x"), CancellationToken.None);

            Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
            Assert.True(IoFile.Exists(Path.Combine(dir, "m.txt")));
        }
        finally { Directory.Delete(baseDir, recursive: true); }
    }

    [Fact]
    public async Task Returns_failed_when_directory_missing_and_creation_disabled()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ibe-missing-" + Guid.NewGuid().ToString("N"));
        var endpoint = new FileOutboundEndpoint(
            new FileOutboundOptions { Directory = dir, FileNameTemplate = "m.txt", CreateDirectory = false }, codec: null);

        var result = await endpoint.SendAsync(Message("c", "x"), CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Failed, result.Outcome);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public async Task Returns_failed_when_the_codec_rejects_the_payload()
    {
        var dir = CreateTempDir();
        try
        {
            var endpoint = new FileOutboundEndpoint(
                new FileOutboundOptions { Directory = dir, FileNameTemplate = "m.txt" }, new Base64Codec());

            var result = await endpoint.SendAsync(Message("c", "!!! not valid base64 !!!"), CancellationToken.None);

            Assert.Equal(DeliveryOutcome.Failed, result.Outcome);   // clean failure, not an unhandled FormatException
            Assert.Empty(Directory.GetFiles(dir));                  // nothing written; temp cleaned up
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ibe-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
