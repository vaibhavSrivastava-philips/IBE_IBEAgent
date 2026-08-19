using System.Text;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class BlobEnvelopeExtractStageTests
{
    [Fact]
    public async Task Extracts_content_and_sets_the_name_and_path_headers()
    {
        var raw = Encoding.UTF8.GetBytes("PDF-BYTES");
        var envelope = $"{{\"filename\":\"report.pdf\",\"destinationpath\":\"out/sub\",\"filecontent\":\"{Convert.ToBase64String(raw)}\"}}";
        var ctx = MessageContextBuilder.Create(payload: envelope);

        var result = await new BlobEnvelopeExtractStage().ProcessAsync(ctx);

        Assert.False(result.Filtered);
        Assert.Equal("PDF-BYTES", Encoding.UTF8.GetString(ctx.Payload.Span));   // payload replaced with decoded bytes
        Assert.Equal("report.pdf", ctx.Headers[BlobHeaders.BlobName]);
        Assert.Equal("out/sub", ctx.Headers[BlobHeaders.BlobPath]);            // destinationpath surfaced for a sink to honor
    }

    [Fact]
    public async Task Non_envelope_payload_passes_through_unchanged()
    {
        var ctx = MessageContextBuilder.Create(payload: "MSH|^~\\&|SENDER");

        var result = await new BlobEnvelopeExtractStage().ProcessAsync(ctx);

        Assert.False(result.Filtered);
        Assert.Equal("MSH|^~\\&|SENDER", Encoding.UTF8.GetString(ctx.Payload.Span));
        Assert.False(ctx.Headers.ContainsKey(BlobHeaders.BlobName));
    }
}
