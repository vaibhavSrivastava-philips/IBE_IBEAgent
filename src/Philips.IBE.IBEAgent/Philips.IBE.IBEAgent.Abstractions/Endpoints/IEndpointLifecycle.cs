namespace Philips.IBE.IBEAgent.Abstractions;

public interface IEndpointLifecycle
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
