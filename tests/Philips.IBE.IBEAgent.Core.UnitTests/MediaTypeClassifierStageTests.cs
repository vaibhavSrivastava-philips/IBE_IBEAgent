using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class MediaTypeClassifierStageTests
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
    };

    [Fact]
    public async Task Sets_content_type_from_the_forwarded_source_file_extension()
    {
        var ctx = NewContext(new(StringComparer.Ordinal) { [ForwardHeaders.Key("filesourcepath")] = "report.pdf" });

        var result = await new MediaTypeClassifierStage(Map).ProcessAsync(ctx);

        Assert.Equal(StageResult.Continue, result);
        Assert.Equal("application/pdf", ctx.Headers[ContentHeaders.ContentType]);
    }

    [Fact]
    public async Task Prefers_the_envelope_blob_name_over_the_forwarded_path()
    {
        var ctx = NewContext(new(StringComparer.Ordinal)
        {
            [BlobHeaders.BlobName] = "scan.png",
            [ForwardHeaders.Key("filesourcepath")] = "envelope.json",
        });

        await new MediaTypeClassifierStage(Map).ProcessAsync(ctx);

        Assert.Equal("image/png", ctx.Headers[ContentHeaders.ContentType]);
    }

    [Fact]
    public async Task Does_not_override_an_already_set_content_type()
    {
        var ctx = NewContext(new(StringComparer.Ordinal)
        {
            [ContentHeaders.ContentType] = "application/dicom",
            [ForwardHeaders.Key("filesourcepath")] = "img.png",
        });

        await new MediaTypeClassifierStage(Map).ProcessAsync(ctx);

        Assert.Equal("application/dicom", ctx.Headers[ContentHeaders.ContentType]);
    }

    [Fact]
    public async Task Unknown_extension_passes_through_without_setting_a_content_type()
    {
        var ctx = NewContext(new(StringComparer.Ordinal) { [ForwardHeaders.Key("filesourcepath")] = "data.bin" });

        var result = await new MediaTypeClassifierStage(Map).ProcessAsync(ctx);

        Assert.Equal(StageResult.Continue, result);
        Assert.False(ctx.Headers.ContainsKey(ContentHeaders.ContentType));
    }

    [Fact]
    public async Task Empty_map_is_a_no_op()
    {
        var ctx = NewContext(new(StringComparer.Ordinal) { [ForwardHeaders.Key("filesourcepath")] = "report.pdf" });

        await new MediaTypeClassifierStage(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)).ProcessAsync(ctx);

        Assert.False(ctx.Headers.ContainsKey(ContentHeaders.ContentType));
    }

    private static MessageContext NewContext(Dictionary<string, string> headers)
        => new("cid", 1, MessageFormats.Hl7v2, new FakeAckToken(), new RecordingReplyContext(),
            payload: new byte[] { 1, 2, 3 }, headers: headers);
}
