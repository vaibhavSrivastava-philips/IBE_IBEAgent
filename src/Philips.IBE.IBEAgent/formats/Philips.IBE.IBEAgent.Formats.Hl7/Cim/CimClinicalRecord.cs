namespace Philips.IBE.IBEAgent.Formats.Hl7.Cim;

public sealed record CimClinicalRecord
{
    public required string MessageControlId { get; init; }
    public string? MessageType { get; init; }
    public string? EventTimestamp { get; init; }
    public CimPatientRecord Patient { get; init; } = new();
    public CimSourceDeviceRecord SourceDevice { get; init; } = new();
    public CimVisitRecord Visit { get; init; } = new();
    public CimOrderRecord Order { get; init; } = new();
    public IReadOnlyList<CimObservationRecord> Observations { get; init; } = [];
    public IReadOnlyList<CimAlertRecord> Alerts { get; init; } = [];
}

public sealed record CimPatientRecord
{
    public string? PatientId { get; init; }
    public string? FamilyName { get; init; }
    public string? GivenName { get; init; }
    public string? DateOfBirth { get; init; }
    public string? Sex { get; init; }
}

public sealed record CimSourceDeviceRecord
{
    public string? SendingApplication { get; init; }
    public string? SendingFacility { get; init; }
}

public sealed record CimVisitRecord
{
    public string? PatientClass { get; init; }
    public string? Location { get; init; }
    public string? AttendingDoctor { get; init; }
}

public sealed record CimOrderRecord
{
    public string? PlacerOrderNumber { get; init; }
    public string? FillerOrderNumber { get; init; }
    public string? UniversalServiceId { get; init; }
    public string? UniversalServiceText { get; init; }
    public string? RequestedAt { get; init; }
}

public sealed record CimObservationRecord
{
    public required string Identifier { get; init; }
    public string? Text { get; init; }
    public string? ValueType { get; init; }
    public string? Value { get; init; }
    public string? Units { get; init; }
    public string? ReferenceRange { get; init; }
    public string? AbnormalFlags { get; init; }
    public string? Status { get; init; }
    public string? ObservedAt { get; init; }
}

public sealed record CimAlertRecord
{
    public required string Identifier { get; init; }
    public string? Text { get; init; }
    public string? State { get; init; }
    public string? AnnouncedAt { get; init; }
}
