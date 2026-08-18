using System.Globalization;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;

namespace Philips.IBE.IBEAgent.Endpoints.File;

// Resolves an output file name from a template + the message; deterministic given the timestamp.
// Tokens: {timestamp} {correlationId} {messageId} {ext}. The default guarantees uniqueness.
public static class FileNameResolver
{
    public const string DefaultTemplate = "Message_{timestamp}_{correlationId}.{ext}";

    public static string Resolve(
        MessageContext context,
        string? template = null,
        string defaultExtension = "txt",
        DateTime? timestampUtc = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A source-declared name (e.g. from a file envelope, via the blob.name header) overrides the template.
        if (context.Headers.TryGetValue(BlobHeaders.BlobName, out var blobName) && !string.IsNullOrWhiteSpace(blobName))
            return Sanitize(blobName);

        var tpl = string.IsNullOrWhiteSpace(template) ? DefaultTemplate : template;
        var stamp = (timestampUtc ?? DateTime.UtcNow).ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture);

        var name = tpl
            .Replace("{timestamp}", stamp, StringComparison.Ordinal)
            .Replace("{correlationId}", context.CorrelationId, StringComparison.Ordinal)
            .Replace("{messageId}", context.MessageId.ToString("N"), StringComparison.Ordinal)
            .Replace("{ext}", defaultExtension.TrimStart('.'), StringComparison.Ordinal);

        return Sanitize(name);
    }

    // Reduce to a SAFE bare file name: strip any directory components (traversal guard) + invalid chars.
    private static string Sanitize(string value)
    {
        var bare = Path.GetFileName(value);
        foreach (var invalid in Path.GetInvalidFileNameChars())
            bare = bare.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(bare) ? "message" : bare;
    }
}
