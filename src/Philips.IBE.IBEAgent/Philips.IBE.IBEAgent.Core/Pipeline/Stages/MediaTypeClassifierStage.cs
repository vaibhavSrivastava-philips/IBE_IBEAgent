using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// Sets the content.type header from the source file's extension when it isn't already set, using a
// developer-configured extension -> media-type map (catalog "MediaTypes", e.g. ".pdf" -> "application/pdf").
// A header-capable output honors content.type to label the wire content type. Fail-safe: an already-set
// content type, an unknown extension, or a missing source file name all pass through unchanged
// (classification never drops a message or overrides an upstream decision). Opt-in per contract by adding
// "media-type" to a pipeline; the map is the only thing that decides mappings — nothing is hardwired here.
public sealed class MediaTypeClassifierStage : IMessageStage
{
    public const string Name = "media-type";

    private readonly IReadOnlyDictionary<string, string> _extensionToMediaType;

    public MediaTypeClassifierStage(IReadOnlyDictionary<string, string> extensionToMediaType)
        => _extensionToMediaType = extensionToMediaType ?? throw new ArgumentNullException(nameof(extensionToMediaType));

    public Task<StageResult> ProcessAsync(MessageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!HasContentType(context) && ResolveSourceFileName(context) is { } fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (extension.Length > 0 && _extensionToMediaType.TryGetValue(extension, out var mediaType))
                context.Headers[ContentHeaders.ContentType] = mediaType;
        }

        return Task.FromResult(StageResult.Continue);
    }

    private static bool HasContentType(MessageContext context)
        => context.Headers.TryGetValue(ContentHeaders.ContentType, out var value) && !string.IsNullOrWhiteSpace(value);

    // Prefer an envelope-declared name (blob.name); fall back to the file name a File input forwards.
    private static string? ResolveSourceFileName(MessageContext context)
    {
        if (context.Headers.TryGetValue(BlobHeaders.BlobName, out var blobName) && !string.IsNullOrWhiteSpace(blobName))
            return blobName;
        if (context.Headers.TryGetValue(ForwardHeaders.Key("filesourcepath"), out var sourcePath) && !string.IsNullOrWhiteSpace(sourcePath))
            return sourcePath;
        return null;
    }
}
