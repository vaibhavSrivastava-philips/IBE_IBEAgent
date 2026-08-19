using System.Text.Json;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// Generic content stage: if the payload is a "{filename, filecontent(base64), destinationpath?}" envelope,
// decode filecontent into the canonical payload and surface filename/destinationpath as the blob.name /
// blob.path headers for a downstream sink (e.g. File) to honor. Whether blob.path is applied is the sink's
// decision (the File sink gates it behind AllowMessageDirectedPath). Non-envelope payloads pass through
// unchanged. Runs once in the shared pipeline (pre fan-out) where Headers are mutable.
public sealed class BlobEnvelopeExtractStage : IMessageStage
{
    public const string Name = "blob-envelope-extract";

    public Task<StageResult> ProcessAsync(MessageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (TryExtract(context.Payload, out var content, out var name, out var destinationPath))
        {
            context.ReplacePayload(content);
            if (!string.IsNullOrWhiteSpace(name)) context.Headers[BlobHeaders.BlobName] = name!;
            if (!string.IsNullOrWhiteSpace(destinationPath)) context.Headers[BlobHeaders.BlobPath] = destinationPath!;
        }
        return Task.FromResult(StageResult.Continue);
    }

    private static bool TryExtract(ReadOnlyMemory<byte> payload, out byte[] content, out string? name, out string? destinationPath)
    {
        content = [];
        name = null;
        destinationPath = null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("filecontent", out var contentProp) || contentProp.ValueKind != JsonValueKind.String)
                return false;

            content = Convert.FromBase64String(contentProp.GetString()!);
            if (root.TryGetProperty("filename", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                name = nameProp.GetString();
            if (root.TryGetProperty("destinationpath", out var pathProp) && pathProp.ValueKind == JsonValueKind.String)
                destinationPath = pathProp.GetString();
            return true;
        }
        catch (JsonException) { return false; }        // not JSON -> pass through
        catch (FormatException) { return false; }       // filecontent not valid Base64 -> pass through
    }
}
