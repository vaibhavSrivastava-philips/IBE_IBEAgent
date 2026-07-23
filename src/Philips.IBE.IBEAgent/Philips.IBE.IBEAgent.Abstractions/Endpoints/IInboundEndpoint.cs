namespace Philips.IBE.IBEAgent.Abstractions;

public interface IInboundEndpoint          // hosted lifecycle; starts after runtimes, stops first.
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

