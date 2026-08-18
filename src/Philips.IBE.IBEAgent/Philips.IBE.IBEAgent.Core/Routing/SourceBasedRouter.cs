using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// §3.2a — pure routing decision (Strategy). Default: resolve by SourceEndpointId via the
// ContractRegistry's input index. A future ContentBasedRouter (by field/header) still
// resolves to exactly one contract per message (INV-3).
public sealed class SourceBasedRouter : IContractResolver
{
    private readonly ContractRegistry _registry;

    public SourceBasedRouter(ContractRegistry registry) => _registry = registry;

    public IContractRuntime Resolve(MessageContext context) => _registry.ForSource(context.SourceEndpointId);
}
