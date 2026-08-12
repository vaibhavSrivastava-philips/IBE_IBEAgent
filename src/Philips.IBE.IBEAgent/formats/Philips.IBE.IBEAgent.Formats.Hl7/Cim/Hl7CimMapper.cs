using System.Text;

namespace Philips.IBE.IBEAgent.Formats.Hl7.Cim;

public sealed class Hl7CimMapper
{
    public CimClinicalRecord Map(ReadOnlyMemory<byte> payload)
    {
        var message = Encoding.UTF8.GetString(payload.Span);
        var segments = ParseSegments(message);
        var msh = First(segments, "MSH");
        var pid = First(segments, "PID");
        var pv1 = First(segments, "PV1");
        var obr = First(segments, "OBR");
        var observations = segments
            .Where(s => s.Name == "OBX")
            .Select(MapObservation)
            .Where(o => o is not null)
            .Cast<CimObservationRecord>()
            .ToArray();
        var alerts = segments
            .Where(s => s.Name == "AL1" || s.Name == "NTE")
            .Select(MapAlert)
            .Where(a => a is not null)
            .Cast<CimAlertRecord>()
            .ToArray();

        return new CimClinicalRecord
        {
            MessageControlId = Field(msh, 10) ?? Guid.NewGuid().ToString("N"),
            MessageType = Field(msh, 9),
            EventTimestamp = Field(msh, 7),
            Patient = MapPatient(pid),
            SourceDevice = new CimSourceDeviceRecord
            {
                SendingApplication = Field(msh, 3),
                SendingFacility = Field(msh, 4),
            },
            Visit = MapVisit(pv1),
            Order = MapOrder(obr),
            Observations = observations,
            Alerts = alerts,
        };
    }

    private static CimPatientRecord MapPatient(Segment? pid)
    {
        var name = Components(Field(pid, 5));
        return new CimPatientRecord
        {
            PatientId = FirstComponent(Field(pid, 3)),
            FamilyName = Component(name, 0),
            GivenName = Component(name, 1),
            DateOfBirth = Field(pid, 7),
            Sex = Field(pid, 8),
        };
    }

    private static CimVisitRecord MapVisit(Segment? pv1)
    {
        return new CimVisitRecord
        {
            PatientClass = Field(pv1, 2),
            Location = Field(pv1, 3),
            AttendingDoctor = Field(pv1, 7),
        };
    }

    private static CimOrderRecord MapOrder(Segment? obr)
    {
        var service = Components(Field(obr, 4));
        return new CimOrderRecord
        {
            PlacerOrderNumber = FirstComponent(Field(obr, 2)),
            FillerOrderNumber = FirstComponent(Field(obr, 3)),
            UniversalServiceId = Component(service, 0),
            UniversalServiceText = Component(service, 1),
            RequestedAt = Field(obr, 6),
        };
    }

    private static CimObservationRecord? MapObservation(Segment segment)
    {
        var id = Components(Field(segment, 3));
        var identifier = Component(id, 0);
        if (string.IsNullOrWhiteSpace(identifier))
            return null;

        var units = Components(Field(segment, 6));
        return new CimObservationRecord
        {
            Identifier = identifier,
            Text = Component(id, 1),
            ValueType = Field(segment, 2),
            Value = Field(segment, 5),
            Units = Component(units, 0),
            ReferenceRange = Field(segment, 7),
            AbnormalFlags = Field(segment, 8),
            Status = Field(segment, 11),
            ObservedAt = Field(segment, 14),
        };
    }

    private static CimAlertRecord? MapAlert(Segment segment)
    {
        if (segment.Name == "AL1")
        {
            var id = Components(Field(segment, 3));
            var identifier = Component(id, 0);
            if (string.IsNullOrWhiteSpace(identifier))
                return null;

            return new CimAlertRecord
            {
                Identifier = identifier,
                Text = Component(id, 1),
                State = Field(segment, 6),
            };
        }

        var text = Field(segment, 3);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return new CimAlertRecord
        {
            Identifier = "NTE",
            Text = text,
        };
    }

    private static Segment? First(IEnumerable<Segment> segments, string name)
        => segments.FirstOrDefault(s => s.Name == name);

    private static string? Field(Segment? segment, int fieldNumber)
    {
        if (segment is null) return null;
        var index = segment.Name == "MSH" ? fieldNumber - 1 : fieldNumber;
        return index >= 0 && index < segment.Fields.Length && !string.IsNullOrWhiteSpace(segment.Fields[index])
            ? segment.Fields[index]
            : null;
    }

    private static string? FirstComponent(string? value) => Component(Components(value), 0);

    private static string[] Components(string? value)
        => string.IsNullOrWhiteSpace(value) ? [] : value.Split('^');

    private static string? Component(string[] components, int index)
        => index >= 0 && index < components.Length && !string.IsNullOrWhiteSpace(components[index]) ? components[index] : null;

    private static IReadOnlyList<Segment> ParseSegments(string message)
        => message
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|'))
            .Where(fields => fields.Length > 0 && fields[0].Length == 3)
            .Select(fields => new Segment(fields[0], fields))
            .ToArray();

    private sealed record Segment(string Name, string[] Fields);
}
