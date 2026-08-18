using System.Security.Cryptography;
using System.Text;

namespace Philips.IBE.IBEAgent.Formats.Hl7.Cim;

public static class CimAvroSchema
{
    public const string Name = "CimClinicalRecord";
    public const string Namespace = "Philips.IBE.IBEAgent.Cim";

    public const string SchemaJson = """
        {
          "type": "record",
          "name": "CimClinicalRecord",
          "namespace": "Philips.IBE.IBEAgent.Cim",
          "fields": [
            { "name": "messageControlId", "type": "string" },
            { "name": "messageType", "type": ["null", "string"], "default": null },
            { "name": "eventTimestamp", "type": ["null", "string"], "default": null },
            {
              "name": "patient",
              "type": {
                "type": "record",
                "name": "CimPatientRecord",
                "fields": [
                  { "name": "patientId", "type": ["null", "string"], "default": null },
                  { "name": "familyName", "type": ["null", "string"], "default": null },
                  { "name": "givenName", "type": ["null", "string"], "default": null },
                  { "name": "dateOfBirth", "type": ["null", "string"], "default": null },
                  { "name": "sex", "type": ["null", "string"], "default": null }
                ]
              }
            },
            {
              "name": "sourceDevice",
              "type": {
                "type": "record",
                "name": "CimSourceDeviceRecord",
                "fields": [
                  { "name": "sendingApplication", "type": ["null", "string"], "default": null },
                  { "name": "sendingFacility", "type": ["null", "string"], "default": null }
                ]
              }
            },
            {
              "name": "visit",
              "type": {
                "type": "record",
                "name": "CimVisitRecord",
                "fields": [
                  { "name": "patientClass", "type": ["null", "string"], "default": null },
                  { "name": "location", "type": ["null", "string"], "default": null },
                  { "name": "attendingDoctor", "type": ["null", "string"], "default": null }
                ]
              }
            },
            {
              "name": "order",
              "type": {
                "type": "record",
                "name": "CimOrderRecord",
                "fields": [
                  { "name": "placerOrderNumber", "type": ["null", "string"], "default": null },
                  { "name": "fillerOrderNumber", "type": ["null", "string"], "default": null },
                  { "name": "universalServiceId", "type": ["null", "string"], "default": null },
                  { "name": "universalServiceText", "type": ["null", "string"], "default": null },
                  { "name": "requestedAt", "type": ["null", "string"], "default": null }
                ]
              }
            },
            {
              "name": "observations",
              "type": {
                "type": "array",
                "items": {
                  "type": "record",
                  "name": "CimObservationRecord",
                  "fields": [
                    { "name": "identifier", "type": "string" },
                    { "name": "text", "type": ["null", "string"], "default": null },
                    { "name": "valueType", "type": ["null", "string"], "default": null },
                    { "name": "value", "type": ["null", "string"], "default": null },
                    { "name": "units", "type": ["null", "string"], "default": null },
                    { "name": "referenceRange", "type": ["null", "string"], "default": null },
                    { "name": "abnormalFlags", "type": ["null", "string"], "default": null },
                    { "name": "status", "type": ["null", "string"], "default": null },
                    { "name": "observedAt", "type": ["null", "string"], "default": null }
                  ]
                }
              },
              "default": []
            },
            {
              "name": "alerts",
              "type": {
                "type": "array",
                "items": {
                  "type": "record",
                  "name": "CimAlertRecord",
                  "fields": [
                    { "name": "identifier", "type": "string" },
                    { "name": "text", "type": ["null", "string"], "default": null },
                    { "name": "state", "type": ["null", "string"], "default": null },
                    { "name": "announcedAt", "type": ["null", "string"], "default": null }
                  ]
                }
              },
              "default": []
            }
          ]
        }
        """;

    public static string Sha256Fingerprint { get; } = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(SchemaJson))).ToLowerInvariant();
}
