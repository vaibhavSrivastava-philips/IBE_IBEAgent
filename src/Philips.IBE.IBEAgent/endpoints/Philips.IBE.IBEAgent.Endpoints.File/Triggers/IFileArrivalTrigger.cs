namespace Philips.IBE.IBEAgent.Endpoints.File;

// Detection seam: decides WHEN to scan (poll tick now; a watcher/hybrid later). The endpoint owns
// WHAT to do on each tick (scan -> read -> dispatch). Start invokes onTick until Stop/cancel.
public interface IFileArrivalTrigger
{
    Task StartAsync(Func<CancellationToken, Task> onTick, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
