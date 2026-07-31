namespace Philips.IBE.IBEAgent.Configuration;

// §8 — thrown when a contract references a catalog Template/Format name that does not exist, so the
// contract cannot be flattened into concrete per-leg values. Structural catalog/contract consistency
// (pipeline/codec resolution) is reported separately by the validators as batched errors.
public sealed class ContractResolutionException(string contractName, string reason)
    : Exception($"Contract '{contractName}' {reason}")
{
    public string ContractName { get; } = contractName;
}
