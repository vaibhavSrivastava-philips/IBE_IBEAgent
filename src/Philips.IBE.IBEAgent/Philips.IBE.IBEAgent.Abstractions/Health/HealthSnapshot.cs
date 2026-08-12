namespace Philips.IBE.IBEAgent.Abstractions;

public sealed record HealthSnapshot(
    string Component,
    HealthStatus Status,
    string? Detail,
    DateTimeOffset CheckedAtUtc);
