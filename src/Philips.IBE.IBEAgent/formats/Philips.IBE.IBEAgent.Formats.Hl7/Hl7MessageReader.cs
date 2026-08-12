using System.Text;

namespace Philips.IBE.IBEAgent.Formats.Hl7;

internal sealed class Hl7MessageReader
{
    private readonly IReadOnlyList<Segment> _segments;

    private Hl7MessageReader(IReadOnlyList<Segment> segments)
    {
        _segments = segments;
    }

    public static Hl7MessageReader Parse(ReadOnlyMemory<byte> payload)
    {
        var text = Encoding.UTF8.GetString(payload.Span);
        var segments = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|'))
            .Where(fields => fields.Length > 0 && fields[0].Length == 3)
            .Select(fields => new Segment(fields[0], fields))
            .ToArray();
        return new Hl7MessageReader(segments);
    }

    public string? MessageType => Field("MSH", 9);
    public string? MessageControlId => Field("MSH", 10);

    public string? Field(string segmentName, int fieldNumber, int occurrence = 0)
    {
        if (fieldNumber <= 0 || occurrence < 0) return null;
        var segment = _segments.Where(s => s.Name == segmentName).Skip(occurrence).FirstOrDefault();
        if (segment is null) return null;

        var index = segment.Name == "MSH" ? fieldNumber - 1 : fieldNumber;
        return index >= 0 && index < segment.Fields.Length && !string.IsNullOrWhiteSpace(segment.Fields[index])
            ? segment.Fields[index]
            : null;
    }

    private sealed record Segment(string Name, string[] Fields);
}
