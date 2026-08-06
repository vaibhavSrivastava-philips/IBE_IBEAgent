using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.Persistence;
using Philips.IBE.IBEAgent.Security;

namespace Philips.IBE.IBEAgent.Service;

// §3.10/§14 — the composition root: config -> compile IContractRuntimes + legs -> register
// endpoints -> hand everything to AgentRuntimeHost. This is the ONE place topology is assembled;
// everything it calls implements an interface frozen in Phase 1.
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIbeAgentEngine(this IServiceCollection services, IConfiguration configuration)
    {
        // Config sections (merged from appsettings.json + catalogData.json + contractData.json in /config):
        //   Catalog   -> developer-owned pipelines + codecs
        //   Endpoints -> FSE-owned comm points   Contracts -> FSE-owned contract topology (a flat array)
        var catalog = configuration.GetSection("Catalog").Get<CatalogOptions>()
            ?? throw new InvalidOperationException("Required configuration section 'Catalog' is missing.");
        var contracts = configuration.GetSection("Contracts").Get<List<ContractOptions>>()
            ?? throw new InvalidOperationException("Required configuration section 'Contracts' is missing.");
        var endpoints = configuration.GetSection("Endpoints").Get<AgentEndpointsOptions>()
            ?? throw new InvalidOperationException("Required configuration section 'Endpoints' is missing.");
        var forwardOptions = configuration.GetSection("Forward").Get<ForwardOptions>() ?? new ForwardOptions();

        // §3.9 — the in-process store-and-forward buffer. Only AtLeastOnce legs use it; the
        // ForwardWorker is only registered as a hosted service when this host is the active owner.
        var protector = DataProtectorFactory.Create();
        var forwardStore = new InMemoryForwardStore(protector);
        services.AddSingleton(protector);
        services.AddSingleton<IForwardStore>(forwardStore);

        // §3.10 — compile the whole topology ONCE, deferred to first resolve so the ComponentRegistry
        // (and the HL7 ack formatter's logger) use the host's real ILoggerFactory. Materialized at
        // host start when AgentRuntimeHost resolves the runtimes/endpoints — still fail-fast.
        services.AddSingleton(sp => CompiledEngine.Build(
            catalog, contracts, endpoints, forwardStore, protector, sp.GetRequiredService<ILoggerFactory>()));

        services.AddSingleton<IReadOnlyList<IContractRuntime>>(sp => sp.GetRequiredService<CompiledEngine>().Runtimes);
        services.AddSingleton<IReadOnlyList<IInboundEndpoint>>(sp => sp.GetRequiredService<CompiledEngine>().InboundEndpoints);
        services.AddHostedService<AgentRuntimeHost>();

        // §3.9 — this host owns the ForwardWorker only when config says InProcess (default).
        // The out-of-process ForwardService host wires its own replay targets in Phase 7+.
        if (forwardOptions.Owner == ForwardOwner.InProcess)
            services.AddForwardWorker(configuration, sp => sp.GetRequiredService<CompiledEngine>().ReplayTargets);

        return services;
    }
}
