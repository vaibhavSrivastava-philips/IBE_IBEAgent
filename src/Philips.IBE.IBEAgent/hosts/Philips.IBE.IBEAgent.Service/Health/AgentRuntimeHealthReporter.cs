using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Service;

public sealed class AgentRuntimeHealthReporter : IHealthReporter
{
    private readonly object _gate = new();
    private HealthStatus _status = HealthStatus.Degraded;
    private string? _detail = "Agent runtime has not started.";
    private DateTimeOffset _checkedAtUtc = DateTimeOffset.UtcNow;

    public void ReportStarted(int contractCount, int inboundEndpointCount)
        => Set(HealthStatus.Healthy, $"Running {contractCount} contract(s), {inboundEndpointCount} inbound endpoint(s).");

    public void ReportStopping()
        => Set(HealthStatus.Degraded, "Agent runtime is stopping.");

    public void ReportStopped()
        => Set(HealthStatus.Unhealthy, "Agent runtime is stopped.");

    public void ReportFailed(string detail)
        => Set(HealthStatus.Unhealthy, detail);

    public HealthSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new HealthSnapshot("agent-runtime", _status, _detail, _checkedAtUtc);
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
