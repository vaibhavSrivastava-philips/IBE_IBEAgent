using System.Text;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// Universal content codec: the canonical payload is Base64 TEXT; Encode decodes it to the raw bytes
// the destination wants. Reusable by any outbound endpoint (File/HTTP/S3...). A payload that is not
// valid Base64 throws (a leg that names this codec is declaring its producer sends Base64).
public sealed class Base64Codec : IMessageCodec
{
    public ReadOnlyMemory<byte> Encode(MessageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var text = Encoding.UTF8.GetString(context.Payload.Span).Trim();
        return Convert.FromBase64String(text);
    }
}
