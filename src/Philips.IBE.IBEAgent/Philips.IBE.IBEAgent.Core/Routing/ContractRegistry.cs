using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// §3.2b — compiled-contract lookup. A concrete class (not behind a cross-layer interface):
// unlike the Router it has one implementation and no planned alternative. Holds every
// compiled IContractRuntime and the inputCommPointId -> IContractRuntime index (O(1)).
public sealed class ContractRegistry
{
    private readonly Dictionary<int, IContractRuntime> _byInput = [];

    // Registers a compiled contract for every input comm point it owns.
    // Throws if an input id is already claimed by another contract (INV-3: one input -> one contract).
    public void Register(IContractRuntime runtime, IEnumerable<int> inputIds)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        foreach (var inputId in inputIds)
        {
            if (!_byInput.TryAdd(inputId, runtime))
                throw new InvalidOperationException(
                    $"Input comm point {inputId} is already routed to a contract (INV-3: one input -> one contract).");
        }
    }

    // Queried by the Router; makes no decisions itself.
    public IContractRuntime ForSource(int sourceEndpointId)
    {
        if (!_byInput.TryGetValue(sourceEndpointId, out var runtime))
            throw new KeyNotFoundException($"No contract is registered for input comm point {sourceEndpointId}.");
        return runtime;
    }

    public bool TryGetForSource(int sourceEndpointId, out IContractRuntime? runtime)
        => _byInput.TryGetValue(sourceEndpointId, out runtime);
}
