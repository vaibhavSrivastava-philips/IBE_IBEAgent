using Philips.IBE.IBEAgent.Abstractions;
using IoFile = System.IO.File;   // the enclosing namespace ends in ".File", which shadows System.IO.File

namespace Philips.IBE.IBEAgent.Endpoints.File;

// §3.7 — File OUTPUT leg: encode via the leg's codec (or raw payload), then publish ONE file
// atomically (write a temp file in the same directory, then move into place). Durability/retry is
// the leg's store-and-forward, not this endpoint's.
public sealed class FileOutboundEndpoint : IOutboundEndpoint
{
    private readonly FileOutboundOptions _options;
    private readonly IMessageCodec? _codec;

    public FileOutboundEndpoint(FileOutboundOptions options, IMessageCodec? codec)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _codec = codec;
    }

    public async Task<DeliveryResult> SendAsync(MessageContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string? tempPath = null;
        try
        {
            var wire = _codec?.Encode(context) ?? context.Payload;

            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_options.Directory));
            if (_options.CreateDirectory)
                Directory.CreateDirectory(root);

            var fileName = FileNameResolver.Resolve(context, _options.FileNameTemplate, _options.DefaultExtension);
            var targetPath = Path.GetFullPath(Path.Combine(root, fileName));

            // Defense-in-depth: the name is already sanitized to a bare file name; assert the final
            // path stays directly inside the configured directory (no traversal).
            if (!string.Equals(Path.GetDirectoryName(targetPath), root, StringComparison.OrdinalIgnoreCase))
                return new DeliveryResult(DeliveryOutcome.Failed, "resolved path escaped the configured directory");

            tempPath = Path.Combine(root, Path.GetRandomFileName() + ".tmp");
            await IoFile.WriteAllBytesAsync(tempPath, wire, cancellationToken);
            IoFile.Move(tempPath, targetPath, overwrite: true);
            tempPath = null;   // published

            return new DeliveryResult(DeliveryOutcome.Delivered);
        }
        // FormatException = the codec rejected a malformed payload (e.g. bad base64) - a delivery failure, not a crash.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FormatException)
        {
            return new DeliveryResult(DeliveryOutcome.Failed, ex.Message);
        }
        finally
        {
            if (tempPath is not null)
            {
                try { IoFile.Delete(tempPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
