using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.Endpoints.Http;
using Philips.IBE.IBEAgent.Endpoints.Tcp;
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
        var catalog = configuration.GetSection("Catalog").Get<CatalogOptions>() ?? new CatalogOptions();
        var contracts = configuration.GetSection("Contracts").Get<List<ContractOptions>>() ?? [];
        var endpoints = configuration.GetSection("Endpoints").Get<AgentEndpointsOptions>() ?? new AgentEndpointsOptions();
        var forwardOptions = configuration.GetSection("Forward").Get<ForwardOptions>() ?? new ForwardOptions();

        var componentRegistry = ComponentRegistryBuilder.Build(endpoints, catalog);

        // §3.9 — the in-process store-and-forward buffer. Only AtLeastOnce legs use it; the
        // ForwardWorker is only registered as a hosted service when this host is the active owner.
        var protector = DataProtectorFactory.Create();
        var forwardStore = new InMemoryForwardStore(protector);
        services.AddSingleton(protector);
        services.AddSingleton<IForwardStore>(forwardStore);

        var compiler = new ContractCompiler(catalog, componentRegistry, forwardStore);

        var contractRegistry = new ContractRegistry();
        var runtimes = new List<IContractRuntime>();
        var replayTargets = new List<KeyValuePair<int, IReplayTarget>>();
        var replyPoliciesBySource = new Dictionary<int, (IAckStrategy Strategy, TimeSpan Timeout)>();

        foreach (var contract in contracts)
        {
            // §8 — flatten the FSE contract against the developer catalog (Template -> shared
            // Pipeline + per-leg Format) so everything below sees concrete Encoding / batch codec.
            var resolved = ContractTemplateResolver.Resolve(contract, catalog);

            var runtime = compiler.Compile(resolved);
            var inputIds = ContractOptionsValidator.ResolveInputs(resolved).Select(i => i.InputId).ToList();
            contractRegistry.Register(runtime, inputIds);
            runtimes.Add(runtime);

            foreach (var leg in runtime.Legs)
                replayTargets.Add(new KeyValuePair<int, IReplayTarget>(leg.OutputId, leg));

            // §6/§8 — one reply mode per contract (Ack XOR Response), shared by all its inputs.
            var policy = AckStrategyResolver.Resolve(resolved, componentRegistry);
            foreach (var inputId in inputIds)
                replyPoliciesBySource[inputId] = policy;
        }

        var router = new SourceBasedRouter(contractRegistry);
        var dispatcher = new Dispatcher(router);

        // §6/§8 — per-source reply policy resolved per contract (Normal | Enhanced ack | Response).
        var replyContextFactory = new PerSourceReplyContextFactory(replyPoliciesBySource);

        var inboundEndpoints = new List<IInboundEndpoint>();
        foreach (var tcp in endpoints.TcpInbound)
            inboundEndpoints.Add(new TcpInboundEndpoint(tcp, dispatcher, replyContextFactory));
        foreach (var http in endpoints.HttpInbound)
            inboundEndpoints.Add(new HttpInboundEndpoint(http, dispatcher, replyContextFactory));

        services.AddSingleton<IReadOnlyList<IContractRuntime>>(runtimes);
        services.AddSingleton<IReadOnlyList<IInboundEndpoint>>(inboundEndpoints);
        services.AddHostedService<AgentRuntimeHost>();

        // §3.9 — this host owns the ForwardWorker only when config says InProcess (default).
        // The out-of-process ForwardService host wires its own replay targets in Phase 7+.
        if (forwardOptions.Owner == ForwardOwner.InProcess)
            services.AddForwardWorker(configuration, replayTargets);

        return services;
    }
}
