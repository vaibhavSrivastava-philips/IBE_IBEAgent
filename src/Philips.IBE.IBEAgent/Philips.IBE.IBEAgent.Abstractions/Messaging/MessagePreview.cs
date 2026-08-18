using System.Text;

namespace Philips.IBE.IBEAgent.Abstractions;

// Renders a payload as a single-line UTF-8 preview for TRACE-level logging: segment terminators
// (CR/LF, e.g. HL7 segment breaks) collapse to a space so a multi-segment message stays on one log
// line. ONLY call under an IsEnabled(LogLevel.Trace) guard — it allocates a string the size of the
// message, which is why full-message logging is confined to the deepest (Trace) level.
public static class MessagePreview
{
    public static string ForLog(ReadOnlySpan<byte> payload)
        => payload.IsEmpty ? string.Empty : Encoding.UTF8.GetString(payload).ReplaceLineEndings(" ");
}
