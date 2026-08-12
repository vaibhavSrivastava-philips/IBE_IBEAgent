using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Philips.IBE.IBEAgent.Configuration;

namespace Philips.IBE.IBEAgent.Service;

internal sealed class ReloadableAgentRuntimeHost(
    IConfiguration configuration,
    ReloadableEngineManager manager,
    EngineReloadOptions options,
    ILogger<ReloadableAgentRuntimeHost> logger) : BackgroundService
{
    private IDisposable? _changeRegistration;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await manager.LoadInitialAsync(ReadCatalog(), ReadContracts(), ReadEndpoints(), stoppingToken);
        logger.LogInformation("IBE Agent reloadable runtime started with engine snapshot version {Version}.", manager.Version);

        _changeRegistration = ChangeToken.OnChange(configuration.GetReloadToken, () => _ = ReloadAfterDebounceAsync(stoppingToken));

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }
    }

    private async Task ReloadAfterDebounceAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(Math.Max(0, options.DebounceMilliseconds), stoppingToken); }
        catch (OperationCanceledException) { return; }

        await _reloadGate.WaitAsync(stoppingToken);
        try
        {
            var reloaded = await manager.TryReloadAsync(ReadCatalog(), ReadContracts(), ReadEndpoints(), stoppingToken);
            if (reloaded)
                logger.LogInformation("Configuration reload activated engine snapshot version {Version}.", manager.Version);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { _reloadGate.Release(); }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _changeRegistration?.Dispose();
        await manager.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _changeRegistration?.Dispose();
        _reloadGate.Dispose();
        base.Dispose();
    }

    private CatalogOptions ReadCatalog()
        => configuration.GetSection("Catalog").Get<CatalogOptions>()
            ?? throw new InvalidOperationException("Required configuration section 'Catalog' is missing.");

    private List<ContractOptions> ReadContracts()
        => configuration.GetSection("Contracts").Get<List<ContractOptions>>()
            ?? throw new InvalidOperationException("Required configuration section 'Contracts' is missing.");

    private AgentEndpointsOptions ReadEndpoints()
        => configuration.GetSection("Endpoints").Get<AgentEndpointsOptions>()
            ?? throw new InvalidOperationException("Required configuration section 'Endpoints' is missing.");
}
