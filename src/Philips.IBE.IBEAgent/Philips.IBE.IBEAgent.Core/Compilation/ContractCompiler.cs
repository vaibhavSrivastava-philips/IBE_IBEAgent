using Microsoft.Extensions.Logging;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;

namespace Philips.IBE.IBEAgent.Core;

// §3.10/§14 — config -> IContractRuntime + legs. Runs the structural + cross-reference validators
// first (fail fast, batched errors) then wires per-input channels, the shared pipeline, and each
// DeliveryLeg's queue/codec/endpoint through the ComponentRegistry. No processing logic lives here —
// purely name -> instance + topology assembly.
public sealed class ContractCompiler
{
    private readonly CatalogOptions _catalog;
    private readonly ComponentRegistry _registry;
    private readonly IForwardStore? _forwardStore;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly MessageChannelFactory _channelFactory;

    public ContractCompiler(CatalogOptions catalog, ComponentRegistry registry, IForwardStore? forwardStore = null, ILoggerFactory? loggerFactory = null, string? durableChannelRoot = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _forwardStore = forwardStore;
        _loggerFactory = loggerFactory;
        _channelFactory = new MessageChannelFactory(durableChannelRoot ?? Path.Combine(Path.GetTempPath(), "ibe-agent-durable-channels"));
    }

    public ContractRuntime Compile(ContractOptions contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var errors = new List<string>();
        errors.AddRange(ContractOptionsValidator.Validate(contract).Errors);
        errors.AddRange(CatalogOptionsValidator.Validate(_catalog).Errors);
        errors.AddRange(ContractCatalogCrossValidator.Validate(contract, _catalog).Errors);
        if (errors.Count > 0)
            throw new ContractCompilationException(contract.Name, errors);

        var inputs = ContractOptionsValidator.ResolveInputs(contract);
        var ingressQueues = inputs.ToDictionary(
            i => i.InputId,
            i => _channelFactory.Create(i.Channel, $"contract-{contract.Name}-input-{i.InputId}", durable: false));

        var pipeline = PipelineBuilder.Build(contract.Pipeline, _catalog, _registry);

        var legs = contract.Outputs.Select(o => BuildLeg(o, contract.Name)).ToList();

        return new ContractRuntime(ingressQueues, pipeline, legs, _loggerFactory?.CreateLogger<ContractRuntime>(), contract.Name);
    }

    private DeliveryLeg BuildLeg(OutputOptions output, string contractName)
    {
        var queue = _channelFactory.Create(output.Channel, $"contract-{contractName}-output-{output.OutputId}", output.DeliveryGuarantee == DeliveryGuarantee.AtLeastOnce);
        var endpoint = _registry.CreateOutboundEndpoint(output);
        var fromInputIds = output.FromInputIds is { Count: > 0 } ids
            ? (IReadOnlySet<int>)ids.ToHashSet()
            : null;

        // §3.9: only AtLeastOnce legs use the store — AtMostOnce legs do not persist on failure
        // (the source resends on a missing ack).
        var forward = output.DeliveryGuarantee == DeliveryGuarantee.AtLeastOnce ? _forwardStore : null;

        return new DeliveryLeg(output.OutputId, output.Required, queue, endpoint, fromInputIds, output.RouteWhen, forward, _loggerFactory?.CreateLogger<DeliveryLeg>(), contractName);
    }
}
