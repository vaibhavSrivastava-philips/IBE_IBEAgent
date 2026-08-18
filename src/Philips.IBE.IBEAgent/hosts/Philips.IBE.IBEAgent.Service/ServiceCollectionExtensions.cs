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
        var reloadOptions = configuration.GetSection("EngineReload").Get<EngineReloadOptions>() ?? new EngineReloadOptions();

        services.AddSingleton<AgentRuntimeHealthReporter>();
        services.AddSingleton<IHealthReporter>(sp => sp.GetRequiredService<AgentRuntimeHealthReporter>());
        services.AddSingleton<IHealthSnapshotProvider, HealthSnapshotProvider>();

        var endpointValidation = AgentEndpointsOptionsValidator.Validate(endpoints);
        if (!endpointValidation.IsValid)
        {
            throw new InvalidOperationException(
                "Endpoint configuration validation failed:" + Environment.NewLine + string.Join(Environment.NewLine, endpointValidation.Errors));
        }

        // §3.9 — store-and-forward buffer (in-memory; only AtLeastOnce legs use it).
        var protector = DataProtectorFactory.Create();
        var forwardStore = new InMemoryForwardStore(protector);
        services.AddSingleton(protector);
        services.AddSingleton<IForwardStore>(forwardStore);

        services.AddSingleton(reloadOptions);

        if (reloadOptions.Enabled)
        {
            services.AddSingleton<ReloadableEngineManager>();
            services.AddHostedService<ReloadableAgentRuntimeHost>();
        }
        else
        {
            // §3.10 — compile the whole topology ONCE, deferred to first resolve so the ComponentRegistry
            // (and the HL7 ack formatter's logger) use the host's real ILoggerFactory. Materialized at
            // host start when AgentRuntimeHost resolves the runtimes/endpoints — still fail-fast.
            services.AddSingleton(sp => CompiledEngine.Build(
                catalog, contracts, endpoints, forwardStore, protector, sp.GetRequiredService<ILoggerFactory>()));

            services.AddSingleton<IReadOnlyList<IContractRuntime>>(sp => sp.GetRequiredService<CompiledEngine>().Runtimes);
            services.AddSingleton<IReadOnlyList<IInboundEndpoint>>(sp => sp.GetRequiredService<CompiledEngine>().InboundEndpoints);
            services.AddSingleton<IReadOnlyList<IEndpointLifecycle>>(sp => sp.GetRequiredService<CompiledEngine>().OutboundEndpointLifecycles);
            services.AddHostedService<AgentRuntimeHost>();
        }

        // §3.9 — this host owns the ForwardWorker only when config says InProcess (default).
        // The out-of-process ForwardService host wires its own replay targets through its composition root.
        if (!reloadOptions.Enabled && forwardOptions.Owner == ForwardOwner.InProcess)
            services.AddForwardWorker(configuration, sp => sp.GetRequiredService<CompiledEngine>().ReplayTargets);

        return services;
    }
}
