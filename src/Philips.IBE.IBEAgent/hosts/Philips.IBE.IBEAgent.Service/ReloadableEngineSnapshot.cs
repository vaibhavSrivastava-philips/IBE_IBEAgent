using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Service;

internal sealed class ReloadableEngineSnapshot(CompiledEngine engine, CancellationTokenSource cancellation) : IAsyncDisposable
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(30);
    private readonly List<Task> _runtimeTasks = [];
    private bool _started;

    public CompiledEngine Engine { get; } = engine;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_started) return;

        foreach (var runtime in Engine.Runtimes)
            _runtimeTasks.Add(runtime.RunAsync(cancellation.Token));

        foreach (var endpoint in Engine.OutboundEndpointLifecycles)
            await endpoint.StartAsync(cancellationToken);

        foreach (var endpoint in Engine.InboundEndpoints)
            await endpoint.StartAsync(cancellationToken);

        _started = true;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_started) return;

        foreach (var endpoint in Engine.InboundEndpoints)
            await endpoint.StopAsync(cancellationToken);

        foreach (var endpoint in Engine.OutboundEndpointLifecycles)
            await endpoint.StopAsync(cancellationToken);

        await cancellation.CancelAsync();

        foreach (var runtime in Engine.Runtimes)
        {
            try { await runtime.DrainAsync(DrainTimeout); }
            catch (OperationCanceledException) { }
        }

        try { await Task.WhenAll(_runtimeTasks).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken); }
        catch (OperationCanceledException) { }
        catch (TimeoutException) { }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        await Engine.DisposeAsync();
        cancellation.Dispose();
    }
}
