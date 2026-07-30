using Microsoft.Extensions.Logging;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.Endpoints.Http;
using Philips.IBE.IBEAgent.Endpoints.Tcp;

namespace Philips.IBE.IBEAgent.Service;

// §3.10/§14 — the compiled topology (runtimes + inbound endpoints + replay targets). Built ONCE at
// host start rather than at registration, so the ComponentRegistry (and the HL7 ack formatter's
// logger) resolve the host's real ILoggerFactory. Still fail-fast: it is materialized before any
// message is processed (when AgentRuntimeHost/ForwardWorker are constructed).
internal sealed class CompiledEngine
{
    public required IReadOnlyList<IContractRuntime> Runtimes { get; init; }
    public required IReadOnlyList<IInboundEndpoint> InboundEndpoints { get; init; }
    public required IReadOnlyList<KeyValuePair<int, IReplayTarget>> ReplayTargets { get; init; }

    public static CompiledEngine Build(
        CatalogOptions catalog,
        IReadOnlyList<ContractOptions> contracts,
        AgentEndpointsOptions endpoints,
        IForwardStore forwardStore,
        ILoggerFactory loggerFactory)
    {
        var componentRegistry = ComponentRegistryBuilder.Build(endpoints, catalog, loggerFactory);
        var compiler = new ContractCompiler(catalog, componentRegistry, forwardStore);

        var contractRegistry = new ContractRegistry();
        var runtimes = new List<IContractRuntime>();
        var replayTargets = new List<KeyValuePair<int, IReplayTarget>>();
        var replyPoliciesBySource = new Dictionary<int, ReplyPolicy>();

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

        // §6/§8 — per-source reply policy resolved per contract (Normal | Enhanced ack | Response).
        var dispatcher = new Dispatcher(new SourceBasedRouter(contractRegistry));
        var replyContextFactory = new PerSourceReplyContextFactory(replyPoliciesBySource);

        var inboundEndpoints = new List<IInboundEndpoint>();
        foreach (var tcp in endpoints.TcpInbound)
            inboundEndpoints.Add(new TcpInboundEndpoint(tcp, dispatcher, replyContextFactory));
        foreach (var http in endpoints.HttpInbound)
            inboundEndpoints.Add(new HttpInboundEndpoint(http, dispatcher, replyContextFactory));

        return new CompiledEngine
        {
            Runtimes = runtimes,
            InboundEndpoints = inboundEndpoints,
            ReplayTargets = replayTargets,
        };
    }
}
