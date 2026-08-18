namespace Philips.IBE.IBEAgent.Endpoints.File;

// Poll-based trigger: fires onTick every interval. Robust over network shares and gives natural
// catch-up (each tick re-scans). A watcher-backed trigger can replace it behind IFileArrivalTrigger.
public sealed class PollingFileTrigger : IFileArrivalTrigger
{
    private readonly TimeSpan _interval;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public PollingFileTrigger(TimeSpan interval) => _interval = interval;

    public Task StartAsync(Func<CancellationToken, Task> onTick, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onTick);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = LoopAsync(onTick, _cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_loop is not null)
            try { await _loop.WaitAsync(cancellationToken); } catch (OperationCanceledException) { }
    }

    // onTick is expected not to throw (the endpoint logs its own scan errors); only cancellation ends the loop.
    private async Task LoopAsync(Func<CancellationToken, Task> onTick, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await onTick(ct);
                await Task.Delay(_interval, ct);
            }
            catch (OperationCanceledException) { break; }
        }
    }
}
