using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.Endpoints.File;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class FileNameResolverTests
{
    private static MessageContext Ctx(string correlationId)
        => new(correlationId, sourceEndpointId: 1, format: MessageFormats.Hl7v2,
               ack: new FakeAckToken(), reply: new RecordingReplyContext());

    [Fact]
    public void Default_template_uses_timestamp_and_correlationId()
    {
        var ts = new DateTime(2026, 8, 4, 13, 5, 9, 123, DateTimeKind.Utc);
        var name = FileNameResolver.Resolve(Ctx("abc123"), template: null, defaultExtension: "txt", timestampUtc: ts);
        Assert.Equal("Message_20260804_130509123_abc123.txt", name);
    }

    [Fact]
    public void Custom_template_tokens_are_substituted()
    {
        var ts = new DateTime(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc);
        var name = FileNameResolver.Resolve(Ctx("cid"), template: "adt_{correlationId}_{timestamp}.hl7", timestampUtc: ts);
        Assert.Equal("adt_cid_20260804_000000000.hl7", name);
    }

    [Fact]
    public void Path_separators_in_tokens_are_neutralized()
    {
        var name = FileNameResolver.Resolve(Ctx("../../etc/passwd"), template: "{correlationId}.txt");
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
        Assert.EndsWith(".txt", name, StringComparison.Ordinal);
    }

    [Fact]
    public void Blob_name_header_overrides_the_template()
    {
        var ctx = new MessageContext("cid", 1, MessageFormats.Hl7v2, new FakeAckToken(), new RecordingReplyContext(),
            headers: new Dictionary<string, string> { [BlobHeaders.BlobName] = "report.pdf" });

        Assert.Equal("report.pdf", FileNameResolver.Resolve(ctx, template: "{correlationId}.txt"));
    }
}
