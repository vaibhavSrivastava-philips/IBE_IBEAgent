using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Philips.IBE.IBEAgent.Service;

// §3.1/§3.3/§14 — the single background task that runs every compiled ContractRuntime for the
// lifetime of the host, and drains them on shutdown before the process exits. Inbound endpoints
// are started/stopped separately (they must start AFTER runtimes and stop FIRST, §3.1).
public sealed class AgentRuntimeHost(
    IReadOnlyList<IContractRuntime> runtimes,
    IReadOnlyList<IInboundEndpoint> inboundEndpoints,
    ILogger<AgentRuntimeHost> logger) : BackgroundService
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var runTasks = runtimes.Select(r => r.RunAsync(stoppingToken)).ToList();

        foreach (var endpoint in inboundEndpoints)
            await endpoint.StartAsync(stoppingToken);

        logger.LogInformation(
            "IBE Agent started: {ContractCount} contract(s), {EndpointCount} inbound endpoint(s).",
            runtimes.Count, inboundEndpoints.Count);

        try
        {
            await Task.WhenAny(runTasks.Append(Task.Delay(Timeout.Infinite, stoppingToken)));
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("IBE Agent stopping: draining {EndpointCount} endpoint(s) and {ContractCount} contract(s).",
            inboundEndpoints.Count, runtimes.Count);

        // Endpoints stop accepting first (§3.1/§3.9 shutdown order), then runtimes drain.
        foreach (var endpoint in inboundEndpoints)
            await endpoint.StopAsync(cancellationToken);

        foreach (var runtime in runtimes)
            await runtime.DrainAsync(DrainTimeout);

        await base.StopAsync(cancellationToken);
        logger.LogInformation("IBE Agent stopped.");
    }
}
