namespace Philips.IBE.IBEAgent.Core;

// The media type of the canonical payload, decided upstream (an HTTP inbound relay or the media-type
// stage) and honored by a header-capable OUTPUT (HTTP maps it onto the request content type). It is
// INTERPRETED, not wire-forwarded via fwd.* — HttpClient keeps content headers (Content-Type) separate
// from request headers, so an output must map this deliberately rather than blindly forward it.
public static class ContentHeaders
{
    public const string ContentType = "content.type";
}
