using Microsoft.Extensions.Logging;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;
using Philips.IBE.IBEAgent.Security;

namespace Philips.IBE.IBEAgent.Service;

internal sealed class ReloadableEngineManager(
    IForwardStore forwardStore,
    IDataProtector protector,
    ILoggerFactory loggerFactory,
    ILogger<ReloadableEngineManager> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ReloadableEngineSnapshot? _current;
    private long _version;

    public CompiledEngine? Current => Volatile.Read(ref _current)?.Engine;
    public long Version => Interlocked.Read(ref _version);

    public async Task LoadInitialAsync(
        CatalogOptions catalog,
        IReadOnlyList<ContractOptions> contracts,
        AgentEndpointsOptions endpoints,
        CancellationToken cancellationToken)
    {
        await ReplaceAsync(catalog, contracts, endpoints, cancellationToken);
    }

    public async Task<bool> TryReloadAsync(
        CatalogOptions catalog,
        IReadOnlyList<ContractOptions> contracts,
        AgentEndpointsOptions endpoints,
        CancellationToken cancellationToken)
    {
        try
        {
            await ReplaceAsync(catalog, contracts, endpoints, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Configuration reload failed; keeping engine snapshot version {Version}.", Version);
            return false;
        }
    }

    private async Task ReplaceAsync(
        CatalogOptions catalog,
        IReadOnlyList<ContractOptions> contracts,
        AgentEndpointsOptions endpoints,
        CancellationToken cancellationToken)
    {
        var validation = AgentEndpointsOptionsValidator.Validate(endpoints);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                "Endpoint configuration validation failed:" + Environment.NewLine + string.Join(Environment.NewLine, validation.Errors));
        }

        var candidate = new ReloadableEngineSnapshot(
            CompiledEngine.Build(catalog, contracts, endpoints, forwardStore, protector, loggerFactory),
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));

        await _gate.WaitAsync(cancellationToken);
        ReloadableEngineSnapshot? previous = null;
        try
        {
            await candidate.StartAsync(cancellationToken);
            previous = _current;
            Volatile.Write(ref _current, candidate);
            Interlocked.Increment(ref _version);
        }
        catch
        {
            await candidate.DisposeAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }

        if (previous is not null)
            await previous.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        var snapshot = Interlocked.Exchange(ref _current, null);
        if (snapshot is not null)
            await snapshot.DisposeAsync();
        _gate.Dispose();
    }
}
