using Philips.IBE.IBEAgent.Configuration;

namespace Philips.IBE.IBEAgent.Configuration.UnitTests;

public sealed class ConfigurationMigratorTests
{
    [Fact]
    public void Normalize_accepts_current_schema_without_changes()
    {
        var document = new AgentConfigurationDocument
        {
            SchemaVersion = ConfigurationSchemaVersion.Current,
            Contracts =
            [
                new ContractOptions
                {
                    Name = "Adt",
                    Inputs = [new InputOptions { InputId = 1 }],
                    Outputs = [new OutputOptions { OutputId = 100 }],
                },
            ],
        };

        var result = ConfigurationMigrator.Normalize(document);

        Assert.True(result.Report.IsSuccessful, string.Join(Environment.NewLine, result.Report.Errors));
        Assert.NotNull(result.Document);
        Assert.Equal(ConfigurationSchemaVersion.Current, result.Document.SchemaVersion);
        Assert.Empty(result.Report.Warnings);
    }

    [Fact]
    public void Normalize_converts_input_ids_shorthand_to_inputs_entries()
    {
        var document = new AgentConfigurationDocument
        {
            SchemaVersion = ConfigurationSchemaVersion.Current,
            Contracts =
            [
                new ContractOptions
                {
                    Name = "LegacyInline",
                    Inputs = [],
                    InputIds = [1, 2],
                    Outputs = [new OutputOptions { OutputId = 100 }],
                },
            ],
        };

        var result = ConfigurationMigrator.Normalize(document);

        Assert.True(result.Report.IsSuccessful, string.Join(Environment.NewLine, result.Report.Errors));
        Assert.Equal([1, 2], result.Document!.Contracts.Single().Inputs.Select(i => i.InputId).ToArray());
        Assert.Contains(result.Report.Warnings, w => w.Contains("InputIds shorthand"));
        Assert.Contains(result.Report.AppliedChanges, c => c.Contains("InputIds -> Inputs"));
    }

    [Fact]
    public void Normalize_merges_input_ids_shorthand_with_explicit_inputs()
    {
        var explicitChannel = new ChannelOptions { Capacity = 10, DegreeOfParallelism = 1, Ordered = true };
        var document = new AgentConfigurationDocument
        {
            SchemaVersion = ConfigurationSchemaVersion.Current,
            Contracts =
            [
                new ContractOptions
                {
                    Name = "MixedLegacyInline",
                    Inputs = [new InputOptions { InputId = 1, Channel = explicitChannel }],
                    InputIds = [1, 2],
                    Outputs = [new OutputOptions { OutputId = 100 }],
                },
            ],
        };

        var result = ConfigurationMigrator.Normalize(document);

        Assert.True(result.Report.IsSuccessful, string.Join(Environment.NewLine, result.Report.Errors));
        var contract = result.Document!.Contracts.Single();
        Assert.Null(contract.InputIds);
        Assert.Equal([1, 2], contract.Inputs.Select(i => i.InputId).ToArray());
        Assert.Same(explicitChannel, contract.Inputs[0].Channel);
        Assert.Contains(result.Report.Warnings, w => w.Contains("InputIds shorthand"));
    }

    [Fact]
    public void Normalize_prefers_response_when_legacy_contract_enabled_ack_and_response()
    {
        var document = new AgentConfigurationDocument
        {
            SchemaVersion = ConfigurationSchemaVersion.Current,
            Contracts =
            [
                new ContractOptions
                {
                    Name = "LegacyHighFidelity",
                    Inputs = [new InputOptions { InputId = 1 }],
                    Acknowledgement = new AckOptions { IsEnabled = true, IsEnhanced = true },
                    Response = new ResponseOptions { IsEnabled = true, FromOutputId = 100 },
                    Outputs = [new OutputOptions { OutputId = 100 }],
                },
            ],
        };

        var result = ConfigurationMigrator.Normalize(document);

        Assert.True(result.Report.IsSuccessful, string.Join(Environment.NewLine, result.Report.Errors));
        Assert.False(result.Document!.Contracts.Single().Acknowledgement.IsEnabled);
        Assert.True(result.Document.Contracts.Single().Response.IsEnabled);
        Assert.Contains(result.Report.Warnings, w => w.Contains("enabled both Acknowledgement and Response"));
        Assert.Contains(result.Report.AppliedChanges, c => c.Contains("Acknowledgement.IsEnabled true -> false"));
    }

    [Fact]
    public void Normalize_selects_sole_required_output_as_response_from_output_id()
    {
        var document = new AgentConfigurationDocument
        {
            SchemaVersion = ConfigurationSchemaVersion.Current,
            Contracts =
            [
                new ContractOptions
                {
                    Name = "LegacyResponder",
                    Inputs = [new InputOptions { InputId = 1 }],
                    Acknowledgement = new AckOptions { IsEnabled = false },
                    Response = new ResponseOptions { IsEnabled = true, FromOutputId = null },
                    Outputs =
                    [
                        new OutputOptions { OutputId = 100, Required = false },
                        new OutputOptions { OutputId = 200, Required = true },
                    ],
                },
            ],
        };

        var result = ConfigurationMigrator.Normalize(document);

        Assert.True(result.Report.IsSuccessful, string.Join(Environment.NewLine, result.Report.Errors));
        Assert.Equal(200, result.Document!.Contracts.Single().Response.FromOutputId);
        Assert.Contains(result.Report.Warnings, w => w.Contains("selected sole required output 200"));
        Assert.Contains(result.Report.AppliedChanges, c => c.Contains("Response.FromOutputId null -> 200"));
    }

    [Fact]
    public void Normalize_rejects_unknown_future_schema_version()
    {
        var document = new AgentConfigurationDocument
        {
            SchemaVersion = ConfigurationSchemaVersion.Current + 1,
        };

        var result = ConfigurationMigrator.Normalize(document);

        Assert.False(result.Report.IsSuccessful);
        Assert.Null(result.Document);
        Assert.Contains(result.Report.Errors, e => e.Contains("newer than supported"));
    }

    [Fact]
    public void Normalize_rejects_invalid_schema_version()
    {
        var document = new AgentConfigurationDocument
        {
            SchemaVersion = 0,
        };

        var result = ConfigurationMigrator.Normalize(document);

        Assert.False(result.Report.IsSuccessful);
        Assert.Null(result.Document);
        Assert.Contains(result.Report.Errors, e => e.Contains("must be greater than zero"));
    }
}
