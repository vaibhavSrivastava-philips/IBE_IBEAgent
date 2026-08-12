using System.Text;
using System.Text.Json;
using Philips.IBE.IBEAgent.Formats.Hl7.Cim;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Formats.Hl7.UnitTests;

public sealed class Hl7CimMapperTests
{
    private const string Message = "MSH|^~\\&|Bedside|ICU|IBE|HOSP|20240101120000||ORU^R01|MSG-1|P|2.5\r" +
                                   "PID|1||PAT-123^^^HOSP^MR||Doe^Jane||19800101|F\r" +
                                   "PV1|1|I|ICU^BED5^A|R|||DOC123^Smith^Alex\r" +
                                   "OBR|1|PLACER-1|FILLER-1|VITALS^Vital signs||20240101115800\r" +
                                   "OBX|1|NM|HR^Heart rate||72|bpm|60-100|N|||F|||20240101115900\r" +
                                   "OBX|2|NM|SPO2^Oxygen saturation||98|%|95-100|N|||F|||20240101115901\r" +
                                   "AL1|1||HIGHHR^High heart rate|||ACTIVE\r";

    [Fact]
    public void Mapper_extracts_patient_device_observations_and_alerts()
    {
        var record = new Hl7CimMapper().Map(Encoding.UTF8.GetBytes(Message));

        Assert.Equal("MSG-1", record.MessageControlId);
        Assert.Equal("ORU^R01", record.MessageType);
        Assert.Equal("20240101120000", record.EventTimestamp);
        Assert.Equal("PAT-123", record.Patient.PatientId);
        Assert.Equal("Doe", record.Patient.FamilyName);
        Assert.Equal("Jane", record.Patient.GivenName);
        Assert.Equal("19800101", record.Patient.DateOfBirth);
        Assert.Equal("F", record.Patient.Sex);
        Assert.Equal("Bedside", record.SourceDevice.SendingApplication);
        Assert.Equal("ICU", record.SourceDevice.SendingFacility);
        Assert.Equal("I", record.Visit.PatientClass);
        Assert.Equal("ICU^BED5^A", record.Visit.Location);
        Assert.Equal("DOC123^Smith^Alex", record.Visit.AttendingDoctor);
        Assert.Equal("PLACER-1", record.Order.PlacerOrderNumber);
        Assert.Equal("FILLER-1", record.Order.FillerOrderNumber);
        Assert.Equal("VITALS", record.Order.UniversalServiceId);
        Assert.Equal("Vital signs", record.Order.UniversalServiceText);
        Assert.Equal("20240101115800", record.Order.RequestedAt);
        Assert.Equal(["HR", "SPO2"], record.Observations.Select(o => o.Identifier).ToArray());
        Assert.Equal("72", record.Observations[0].Value);
        Assert.Equal("bpm", record.Observations[0].Units);
        Assert.Equal("60-100", record.Observations[0].ReferenceRange);
        Assert.Equal("N", record.Observations[0].AbnormalFlags);
        Assert.Equal("F", record.Observations[0].Status);
        Assert.Single(record.Alerts);
        Assert.Equal("HIGHHR", record.Alerts[0].Identifier);
        Assert.Equal("ACTIVE", record.Alerts[0].State);
    }

    [Fact]
    public void CimJsonCodec_emits_deterministic_clinical_record_json()
    {
        var codec = new CimJsonCodec();
        var context = MessageContextBuilder.Create(payload: Message);

        var json = Encoding.UTF8.GetString(codec.Encode(context).Span);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("MSG-1", root.GetProperty("messageControlId").GetString());
        Assert.Equal("ORU^R01", root.GetProperty("messageType").GetString());
        Assert.Equal("PAT-123", root.GetProperty("patient").GetProperty("patientId").GetString());
        Assert.Equal("Bedside", root.GetProperty("sourceDevice").GetProperty("sendingApplication").GetString());
        Assert.Equal("ICU^BED5^A", root.GetProperty("visit").GetProperty("location").GetString());
        Assert.Equal("VITALS", root.GetProperty("order").GetProperty("universalServiceId").GetString());
        Assert.Equal(2, root.GetProperty("observations").GetArrayLength());
        Assert.Equal("HR", root.GetProperty("observations")[0].GetProperty("identifier").GetString());
        Assert.Equal("60-100", root.GetProperty("observations")[0].GetProperty("referenceRange").GetString());
        Assert.Equal("HIGHHR", root.GetProperty("alerts")[0].GetProperty("identifier").GetString());
    }

    [Fact]
    public void CimAvroSchema_matches_clinical_record_shape()
    {
        using var document = JsonDocument.Parse(CimAvroSchema.SchemaJson);
        var root = document.RootElement;

        Assert.Equal("record", root.GetProperty("type").GetString());
        Assert.Equal(CimAvroSchema.Name, root.GetProperty("name").GetString());
        Assert.Equal(CimAvroSchema.Namespace, root.GetProperty("namespace").GetString());

        var fields = root.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(["messageControlId", "messageType", "eventTimestamp", "patient", "sourceDevice", "visit", "order", "observations", "alerts"], fields);
    }

    [Fact]
    public void CimAvroSchema_fingerprint_is_stable_for_compatibility_checks()
    {
        Assert.Equal(64, CimAvroSchema.Sha256Fingerprint.Length);
        Assert.Equal(CimAvroSchema.Sha256Fingerprint, CimAvroSchema.Sha256Fingerprint.ToLowerInvariant());
    }

    [Fact]
    public void CimAvroCodec_emits_avro_object_container_with_cim_schema_metadata()
    {
        var codec = new CimAvroCodec();
        var context = MessageContextBuilder.Create(payload: Message);

        var bytes = codec.Encode(context).ToArray();
        var payloadText = Encoding.UTF8.GetString(bytes);

        Assert.Equal((byte)'O', bytes[0]);
        Assert.Equal((byte)'b', bytes[1]);
        Assert.Equal((byte)'j', bytes[2]);
        Assert.Equal(1, bytes[3]);
        Assert.Contains("avro.schema", payloadText);
        Assert.Contains(CimAvroSchema.Name, payloadText);
        Assert.Contains(CimAvroSchema.Namespace, payloadText);
        Assert.Contains("avro.codec", payloadText);
    }

    [Fact]
    public void CimAvroCodec_encodes_clinical_record_values_in_record_block()
    {
        var codec = new CimAvroCodec();
        var context = MessageContextBuilder.Create(payload: Message);

        var bytes = codec.Encode(context).ToArray();
        var payloadText = Encoding.UTF8.GetString(bytes);

        Assert.Contains("MSG-1", payloadText);
        Assert.Contains("PAT-123", payloadText);
        Assert.Contains("Bedside", payloadText);
        Assert.Contains("ICU^BED5^A", payloadText);
        Assert.Contains("VITALS", payloadText);
        Assert.Contains("HR", payloadText);
        Assert.Contains("60-100", payloadText);
        Assert.Contains("HIGHHR", payloadText);
    }
}
