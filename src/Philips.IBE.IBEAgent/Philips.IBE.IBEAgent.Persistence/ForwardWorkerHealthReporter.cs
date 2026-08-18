using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Persistence;

public sealed class ForwardWorkerHealthReporter : IHealthReporter
{
    private readonly object _gate = new();
    private HealthStatus _status = HealthStatus.Degraded;
    private string? _detail = "Forward worker has not started.";
    private DateTimeOffset _checkedAtUtc = DateTimeOffset.UtcNow;

    public void ReportStarted(int batchSize, int maxAttempts)
        => Set(HealthStatus.Healthy, $"Forward worker running; batch size {batchSize}, max attempts {maxAttempts}.");

    public void ReportSweepFailure(string error)
        => Set(HealthStatus.Degraded, $"Last forward sweep failed: {error}.");

    public void ReportStopped()
        => Set(HealthStatus.Unhealthy, "Forward worker is stopped.");

    public HealthSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new HealthSnapshot("forward-worker", _status, _detail, _checkedAtUtc);
        }
    }

    private void Set(HealthStatus status, string? detail)
    {
        lock (_gate)
        {
            _status = status;
            _detail = detail;
            _checkedAtUtc = DateTimeOffset.UtcNow;
        }
    }
}
