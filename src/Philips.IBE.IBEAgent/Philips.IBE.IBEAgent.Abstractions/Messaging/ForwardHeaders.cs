namespace Philips.IBE.IBEAgent.Abstractions;

// Convention for MessageContext.Headers a header-capable OUTPUT should emit as protocol headers on the
// wire. A producer opts a header in by prefixing its key ("fwd.<name>"); an endpoint with a header
// channel (e.g. HTTP) forwards every prefixed header, stripping the prefix. Transports without a
// header channel (MLLP/TCP) ignore them. Opt-in keeps internal headers off the wire.
public static class ForwardHeaders
{
    public const string Prefix = "fwd.";

    public static string Key(string wireHeaderName) => Prefix + wireHeaderName;

    public static bool TryGetName(string headerKey, out string wireHeaderName)
    {
        if (headerKey.StartsWith(Prefix, StringComparison.Ordinal) && headerKey.Length > Prefix.Length)
        {
            wireHeaderName = headerKey[Prefix.Length..];
            return true;
        }
        wireHeaderName = string.Empty;
        return false;
    }
}
