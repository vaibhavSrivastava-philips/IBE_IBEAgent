namespace Philips.IBE.IBEAgent.Configuration;

public static class ConfigurationMigrator
{
    public static ConfigurationMigrationResult Normalize(AgentConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.SchemaVersion <= 0)
        {
            return new ConfigurationMigrationResult(
                null,
                ConfigurationMigrationReport.Failed(
                    document.SchemaVersion,
                    ["Configuration schema version must be greater than zero."]));
        }

        if (document.SchemaVersion > ConfigurationSchemaVersion.Current)
        {
            return new ConfigurationMigrationResult(
                null,
                ConfigurationMigrationReport.Failed(
                    document.SchemaVersion,
                    [$"Configuration schema version {document.SchemaVersion} is newer than supported version {ConfigurationSchemaVersion.Current}."]));
        }

        var warnings = new List<string>();
        var appliedChanges = new List<string>();
        var normalizedContracts = new List<ContractOptions>();
        foreach (var contract in document.Contracts)
        {
            var normalized = NormalizeContract(contract, warnings, appliedChanges);
            normalizedContracts.Add(normalized);
        }

        var normalizedDocument = document with
        {
            SchemaVersion = ConfigurationSchemaVersion.Current,
            Contracts = normalizedContracts,
        };

        return new ConfigurationMigrationResult(
            normalizedDocument,
            ConfigurationMigrationReport.Success(document.SchemaVersion, warnings, appliedChanges));
    }

    private static ContractOptions NormalizeContract(ContractOptions contract, List<string> warnings, List<string> appliedChanges)
    {
        var normalized = contract;

        if (contract.InputIds is { Count: > 0 })
        {
            warnings.Add($"Contract '{contract.Name}' used InputIds shorthand; normalized it to Inputs entries.");
            appliedChanges.Add($"Contract '{contract.Name}': InputIds -> Inputs.");
            var explicitInputs = contract.Inputs ?? [];
            var explicitIds = explicitInputs.Select(i => i.InputId).ToHashSet();
            var inputs = explicitInputs.Concat(contract.InputIds
                .Where(id => explicitIds.Add(id))
                .Select(id => new InputOptions { InputId = id }))
                .ToArray();

            normalized = normalized with
            {
                Inputs = inputs,
                InputIds = null,
            };
        }

        if (normalized.Response.IsEnabled && normalized.Acknowledgement.IsEnabled)
        {
            warnings.Add($"Contract '{normalized.Name}' enabled both Acknowledgement and Response; disabled Acknowledgement because Response is the explicit request-reply mode.");
            appliedChanges.Add($"Contract '{normalized.Name}': Acknowledgement.IsEnabled true -> false because Response.IsEnabled is true.");
            normalized = normalized with
            {
                Acknowledgement = normalized.Acknowledgement with { IsEnabled = false },
            };
        }

        if (normalized.Response is { IsEnabled: true, FromOutputId: null } && normalized.Outputs.Count(o => o.Required) == 1)
        {
            var responder = normalized.Outputs.Single(o => o.Required).OutputId;
            warnings.Add($"Contract '{normalized.Name}' enabled Response without FromOutputId; selected sole required output {responder}.");
            appliedChanges.Add($"Contract '{normalized.Name}': Response.FromOutputId null -> {responder}.");
            normalized = normalized with
            {
                Response = normalized.Response with { FromOutputId = responder },
            };
        }

        return normalized;
    }
}
