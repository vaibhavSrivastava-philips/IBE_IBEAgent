using Microsoft.Extensions.Logging;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.Endpoints.File;
using Philips.IBE.IBEAgent.Endpoints.Http;
using Philips.IBE.IBEAgent.Endpoints.Tcp;
using Philips.IBE.IBEAgent.Security;

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
        IDataProtector protector,
        ILoggerFactory loggerFactory)
    {
        var log = loggerFactory.CreateLogger<CompiledEngine>();
        log.LogInformation("Compiling IBE agent engine: {ContractCount} contract(s).", contracts.Count);

        var componentRegistry = ComponentRegistryBuilder.Build(endpoints, catalog, loggerFactory);
        var compiler = new ContractCompiler(catalog, componentRegistry, forwardStore, loggerFactory);

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
            var policy = AckStrategyResolver.Resolve(resolved, componentRegistry, loggerFactory);
            foreach (var inputId in inputIds)
                replyPoliciesBySource[inputId] = policy;

            log.LogDebug(
                "Compiled contract {ContractName}: {InputCount} input(s), {OutputCount} output(s).",
                resolved.Name, inputIds.Count, runtime.Legs.Count);
        }

        // §6/§8 — per-source reply policy resolved per contract (Normal | Enhanced ack | Response).
        var dispatcher = new Dispatcher(new SourceBasedRouter(contractRegistry));
        var replyContextFactory = new ReplyContextFactory(replyPoliciesBySource, loggerFactory.CreateLogger<ReplyContext>());

        var inboundEndpoints = new List<IInboundEndpoint>();
        foreach (var tcp in endpoints.TcpInbound)
            inboundEndpoints.Add(new TcpInboundEndpoint(tcp, dispatcher, replyContextFactory, loggerFactory.CreateLogger<TcpInboundEndpoint>()));
        foreach (var http in endpoints.HttpInbound)
            inboundEndpoints.Add(new HttpInboundEndpoint(http, dispatcher, replyContextFactory, loggerFactory.CreateLogger<HttpInboundEndpoint>()));
        foreach (var file in endpoints.FileInbound)
            inboundEndpoints.Add(new FileInboundEndpoint(file, dispatcher, replyContextFactory, trigger: null,
                logger: loggerFactory.CreateLogger<FileInboundEndpoint>(), credential: BuildShareCredential(file, protector)));

        log.LogInformation(
            "Engine compiled: {ContractCount} contract(s), {InboundEndpointCount} inbound endpoint(s), {ReplayTargetCount} replay target(s).",
            runtimes.Count, inboundEndpoints.Count, replayTargets.Count);

        // Surface no-op deployments explicitly: without contracts nothing routes, and without inbound
        // endpoints nothing is ever received. Both are almost always a configuration mistake.
        if (runtimes.Count == 0)
            log.LogWarning("No contracts are configured; the agent will not route any messages.");
        if (inboundEndpoints.Count == 0)
            log.LogWarning("No inbound endpoints are configured; the agent will not receive any messages.");

        return new CompiledEngine
        {
            Runtimes = runtimes,
            InboundEndpoints = inboundEndpoints,
            ReplayTargets = replayTargets,
        };
    }

    // A UNC file source with credentials: decrypt the DPAPI-protected password into a plaintext
    // FileShareCredential the endpoint uses to mount the share. No credentials -> local path -> null.
    private static FileShareCredential? BuildShareCredential(FileInboundOptions file, IDataProtector protector)
    {
        if (string.IsNullOrWhiteSpace(file.Username) || string.IsNullOrWhiteSpace(file.PasswordProtected))
            return null;
        var password = System.Text.Encoding.UTF8.GetString(protector.Unprotect(Convert.FromBase64String(file.PasswordProtected)));
        return new FileShareCredential(file.Username, file.Domain, password);
    }
}
