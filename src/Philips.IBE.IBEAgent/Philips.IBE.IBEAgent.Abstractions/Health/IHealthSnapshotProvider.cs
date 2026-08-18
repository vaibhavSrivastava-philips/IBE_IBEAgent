namespace Philips.IBE.IBEAgent.Abstractions;

public interface IHealthSnapshotProvider
{
    IReadOnlyList<HealthSnapshot> GetSnapshots();
    HealthSnapshot GetOverallSnapshot();
}
