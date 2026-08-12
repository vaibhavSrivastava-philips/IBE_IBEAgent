using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Service;

public sealed class HealthSnapshotProvider(IEnumerable<IHealthReporter> reporters) : IHealthSnapshotProvider
{
    private readonly IReadOnlyList<IHealthReporter> _reporters = reporters.ToArray();

    public IReadOnlyList<HealthSnapshot> GetSnapshots()
        => _reporters.Select(r => r.GetSnapshot()).ToArray();

    public HealthSnapshot GetOverallSnapshot()
    {
        var snapshots = GetSnapshots();
        var status = snapshots.Any(s => s.Status == HealthStatus.Unhealthy)
            ? HealthStatus.Unhealthy
            : snapshots.Any(s => s.Status == HealthStatus.Degraded)
                ? HealthStatus.Degraded
                : HealthStatus.Healthy;

        var detail = snapshots.Count == 0
            ? "No health reporters registered."
            : string.Join("; ", snapshots.Select(s => $"{s.Component}: {s.Status}"));

        return new HealthSnapshot("ibe-agent", status, detail, DateTimeOffset.UtcNow);
    }
}
