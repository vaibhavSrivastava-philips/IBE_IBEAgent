using System.Globalization;
using Efferent.HL7.V2;

namespace Philips.IBE.IBEAgent.Formats.Hl7;

// §3.8/§6 — HL7 v2 ACK mechanics, ported verbatim from the legacy
// Philips.IBE.IBEAgent.Common.Utility.HL7AckGenerator (best-practice: it uses the HL7-V2 library's
// GetACK(), which echoes/swaps MSH and carries the original MSH-10 control id into MSA-2). Kept as a
// small reusable utility so both the single-message formatter (and a future batch formatter) can
// reuse it; the IAckFormatter seam (Hl7SingleAckFormatter) is the engine-facing entry point.
public static class HL7AckGenerator
{
    // Builds an HL7 ACK for the given inbound message. statusResponse=true -> MSA-1 "AA", else "AE".
    public static string GenerateHL7Ack(string message, bool statusResponse)
    {
        var incomingMessage = new Message(message);
        incomingMessage.ParseMessage();
        var ackMessage = incomingMessage.GetACK();

        var ackCode = statusResponse ? "AA" : "AE";
        var msaSegment = ackMessage.DefaultSegment("MSA");
        msaSegment.Fields(1).Value = ackCode;
        return ackMessage.SerializeMessage(false);
    }

    // Builds an HL7 negative ACK via the library's GetNACK: MSA-1 = code (e.g. "AR" application reject),
    // carrying the reason as the MSA text message. Used for an intentional drop (filtered) or an error.
    // Falls back to BuildFallbackNack when GetNACK returns null or when the input cannot be parsed
    // (e.g. raw MLLP ACK frames, keep-alive probes, or binary payloads).
    public static string GenerateHL7Reject(string message, string code, string? reason)
    {
        try
        {
            var incomingMessage = new Message(message);
            incomingMessage.ParseMessage();
            var nack = incomingMessage.GetNACK(code, reason ?? string.Empty);
            if (nack is not null)
                return nack.SerializeMessage(false);

            // GetNACK returns null when MSH lacks enough fields to echo back.
            return BuildFallbackNack(TryGetControlId(incomingMessage), reason ?? $"Application {code}");
        }
        catch
        {
            // Input is not parseable HL7 (e.g. MLLP control frame, binary probe).
            return BuildFallbackNack(string.Empty, reason ?? $"Application {code}");
        }
    }

    // Best-effort MSH-10 (message control id) extraction; returns empty string on any failure.
    private static string TryGetControlId(Message message)
    {
        try { return message.Segments("MSH")[0].Fields(10).Value ?? string.Empty; }
        catch { return string.Empty; }
    }

    // Reads MSA-1 (the acknowledgement code) out of an ACK message — used when relaying a downstream
    // system's own ack (enhanced ack / request-reply).
    public static string ParseAcknowledgement(string message)
    {
        try
        {
            var incomingMessage = new Message(message);
            incomingMessage.ParseMessage();
            var msaSegment = incomingMessage.Segments("MSA")[0];
            return msaSegment.Fields(1).Value;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error parsing acknowledgement: {ex.Message}", ex);
        }
    }

    // Builds a minimal, well-formed HL7 v2 AE (Application Error) NACK for the case where the inbound
    // message can't be parsed to build a proper GetACK() response (MSH can't be echoed and the
    // original MSH-10 control id is unknown). `controlId` stands in for MSA-2; `reason` is placed in
    // MSA-3 (text) and coded via an ERR segment (207 = application internal error, HL7 table 0357).
    // HL7 delimiters/newlines in the inputs are stripped so the generated segments stay valid.
    public static string BuildFallbackNack(string controlId, string reason)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var id = Sanitize(controlId);
        var text = Sanitize(reason);
        return string.Join('\r',
            $"MSH|^~\\&|IBEAgent|IBE|||{timestamp}||ACK|{id}|P|2.5",
            $"MSA|AE|{id}|{text}",
            "ERR|||207^Application internal error^HL70357|E");
    }

    // Strip HL7 field/component/repetition/escape/subcomponent separators and newlines so a
    // free-text value can't break out of its segment/field.
    private static string Sanitize(string value)
        => string.IsNullOrEmpty(value)
            ? string.Empty
            : new string(value.Where(c => c is not ('|' or '^' or '~' or '\\' or '&' or '\r' or '\n')).ToArray());
}
