using Philips.IBE.IBEAgent.Abstractions;
using System.Globalization;
using System.Text;

namespace Philips.IBE.IBEAgent.Formats.Hl7;

public sealed class Hl7BatchAckFormatter : IAckFormatter
{
    private readonly Hl7SingleAckFormatter _single;

    public Hl7BatchAckFormatter(Hl7SingleAckFormatter? single = null)
        => _single = single ?? new Hl7SingleAckFormatter();

    public string Format => MessageFormats.Hl7v2;
    public AckShape Shape => AckShape.Batch;

    public ReadOnlyMemory<byte> Render(MessageContext context, in DeliveryResult result)
    {
        var unit = result.Outcome == DeliveryOutcome.Delivered && !result.ResponsePayload.IsEmpty
            ? Encoding.UTF8.GetString(result.ResponsePayload.Span)
            : Encoding.UTF8.GetString(_single.Render(context, result).Span);

        return Encoding.UTF8.GetBytes(WrapUnits([unit]));
    }

    public ReadOnlyMemory<byte> Render(MessageContext context, IReadOnlyList<DeliveryResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var units = results.Count == 0
            ? [Encoding.UTF8.GetString(_single.Render(context, new DeliveryResult(DeliveryOutcome.Failed, "No delivery results were available for batch acknowledgement.")).Span)]
            : results.Select(result => result.Outcome == DeliveryOutcome.Delivered && !result.ResponsePayload.IsEmpty
                ? Encoding.UTF8.GetString(result.ResponsePayload.Span)
                : Encoding.UTF8.GetString(_single.Render(context, result).Span));

        return Encoding.UTF8.GetBytes(WrapUnits(units));
    }

    private static string WrapUnits(IEnumerable<string> units)
    {
        var normalizedUnits = units.SelectMany(SplitSegments).Where(s => s.StartsWith("MSH|", StringComparison.Ordinal) || s.StartsWith("MSA|", StringComparison.Ordinal) || s.StartsWith("ERR|", StringComparison.Ordinal)).ToArray();
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var segments = new List<string> { $"BHS|^~\\&|IBEAgent||||{timestamp}" };
        segments.AddRange(normalizedUnits);
        segments.Add($"BTS|{normalizedUnits.Count(s => s.StartsWith("MSA|", StringComparison.Ordinal))}");
        return string.Join('\r', segments);
    }

    private static IEnumerable<string> SplitSegments(string message)
        => message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
