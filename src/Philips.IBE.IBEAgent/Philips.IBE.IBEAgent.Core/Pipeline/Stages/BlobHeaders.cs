namespace Philips.IBE.IBEAgent.Core;

// Header keys the blob-envelope stage writes for a downstream file sink to honor (see BlobEnvelopeExtractStage).
// Scoped to that feature — NOT a general header contract; MessageContext.Headers stays an open string map.
public static class BlobHeaders
{
    public const string BlobName = "blob.name";   // source-declared output file name (from a file envelope)
}
