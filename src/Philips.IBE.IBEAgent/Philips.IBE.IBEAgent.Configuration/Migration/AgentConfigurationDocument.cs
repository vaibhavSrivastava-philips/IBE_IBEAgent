namespace Philips.IBE.IBEAgent.Configuration;

public sealed record AgentConfigurationDocument
{
    public int SchemaVersion { get; init; } = ConfigurationSchemaVersion.Current;
    public CatalogOptions Catalog { get; init; } = new();
    public IReadOnlyList<ContractOptions> Contracts { get; init; } = [];
}
