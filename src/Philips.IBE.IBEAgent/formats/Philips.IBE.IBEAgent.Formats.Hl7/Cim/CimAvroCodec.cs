using System.Text;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Formats.Hl7.Cim;

public sealed class CimAvroCodec : IMessageCodec
{
    private static readonly byte[] Magic = [(byte)'O', (byte)'b', (byte)'j', 1];
    private static readonly byte[] SyncMarker = Convert.FromHexString(CimAvroSchema.Sha256Fingerprint[..32]);
    private readonly Hl7CimMapper _mapper;

    public CimAvroCodec(Hl7CimMapper? mapper = null)
    {
        _mapper = mapper ?? new Hl7CimMapper();
    }

    public ReadOnlyMemory<byte> Encode(MessageContext context)
    {
        var record = _mapper.Map(context.Payload);
        using var recordStream = new MemoryStream();
        WriteRecord(recordStream, record);
        var recordBytes = recordStream.ToArray();

        using var output = new MemoryStream();
        output.Write(Magic);
        WriteMetadata(output);
        output.Write(SyncMarker);
        WriteLong(output, 1);
        WriteBytes(output, recordBytes);
        output.Write(SyncMarker);
        return output.ToArray();
    }

    private static void WriteMetadata(Stream stream)
    {
        WriteLong(stream, 2);
        WriteString(stream, "avro.schema");
        WriteBytes(stream, Encoding.UTF8.GetBytes(CimAvroSchema.SchemaJson));
        WriteString(stream, "avro.codec");
        WriteBytes(stream, Encoding.UTF8.GetBytes("null"));
        WriteLong(stream, 0);
    }

    private static void WriteRecord(Stream stream, CimClinicalRecord record)
    {
        WriteString(stream, record.MessageControlId);
        WriteNullableString(stream, record.MessageType);
        WriteNullableString(stream, record.EventTimestamp);
        WritePatient(stream, record.Patient);
        WriteSourceDevice(stream, record.SourceDevice);
        WriteVisit(stream, record.Visit);
        WriteOrder(stream, record.Order);
        WriteObservations(stream, record.Observations);
        WriteAlerts(stream, record.Alerts);
    }

    private static void WritePatient(Stream stream, CimPatientRecord patient)
    {
        WriteNullableString(stream, patient.PatientId);
        WriteNullableString(stream, patient.FamilyName);
        WriteNullableString(stream, patient.GivenName);
        WriteNullableString(stream, patient.DateOfBirth);
        WriteNullableString(stream, patient.Sex);
    }

    private static void WriteSourceDevice(Stream stream, CimSourceDeviceRecord sourceDevice)
    {
        WriteNullableString(stream, sourceDevice.SendingApplication);
        WriteNullableString(stream, sourceDevice.SendingFacility);
    }

    private static void WriteVisit(Stream stream, CimVisitRecord visit)
    {
        WriteNullableString(stream, visit.PatientClass);
        WriteNullableString(stream, visit.Location);
        WriteNullableString(stream, visit.AttendingDoctor);
    }

    private static void WriteOrder(Stream stream, CimOrderRecord order)
    {
        WriteNullableString(stream, order.PlacerOrderNumber);
        WriteNullableString(stream, order.FillerOrderNumber);
        WriteNullableString(stream, order.UniversalServiceId);
        WriteNullableString(stream, order.UniversalServiceText);
        WriteNullableString(stream, order.RequestedAt);
    }

    private static void WriteObservations(Stream stream, IReadOnlyList<CimObservationRecord> observations)
    {
        if (observations.Count == 0)
        {
            WriteLong(stream, 0);
            return;
        }

        WriteLong(stream, observations.Count);
        foreach (var observation in observations)
        {
            WriteString(stream, observation.Identifier);
            WriteNullableString(stream, observation.Text);
            WriteNullableString(stream, observation.ValueType);
            WriteNullableString(stream, observation.Value);
            WriteNullableString(stream, observation.Units);
            WriteNullableString(stream, observation.ReferenceRange);
            WriteNullableString(stream, observation.AbnormalFlags);
            WriteNullableString(stream, observation.Status);
            WriteNullableString(stream, observation.ObservedAt);
        }
        WriteLong(stream, 0);
    }

    private static void WriteAlerts(Stream stream, IReadOnlyList<CimAlertRecord> alerts)
    {
        if (alerts.Count == 0)
        {
            WriteLong(stream, 0);
            return;
        }

        WriteLong(stream, alerts.Count);
        foreach (var alert in alerts)
        {
            WriteString(stream, alert.Identifier);
            WriteNullableString(stream, alert.Text);
            WriteNullableString(stream, alert.State);
            WriteNullableString(stream, alert.AnnouncedAt);
        }
        WriteLong(stream, 0);
    }

    private static void WriteNullableString(Stream stream, string? value)
    {
        if (value is null)
        {
            WriteLong(stream, 0);
            return;
        }

        WriteLong(stream, 1);
        WriteString(stream, value);
    }

    private static void WriteString(Stream stream, string value)
        => WriteBytes(stream, Encoding.UTF8.GetBytes(value));

    private static void WriteBytes(Stream stream, ReadOnlySpan<byte> value)
    {
        WriteLong(stream, value.Length);
        stream.Write(value);
    }

    private static void WriteLong(Stream stream, long value)
    {
        var encoded = (ulong)((value << 1) ^ (value >> 63));
        while ((encoded & ~0x7FUL) != 0)
        {
            stream.WriteByte((byte)((encoded & 0x7F) | 0x80));
            encoded >>= 7;
        }
        stream.WriteByte((byte)encoded);
    }
}
