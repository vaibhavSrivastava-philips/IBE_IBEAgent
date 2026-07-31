namespace Philips.IBE.IBEAgent.Configuration;

// §8 — root of contractData.json (FSE-owned): the set of contracts the agent compiles at startup.
public sealed record ContractCatalogOptions
{
    public IReadOnlyList<ContractOptions> Contracts { get; init; } = [];
}
