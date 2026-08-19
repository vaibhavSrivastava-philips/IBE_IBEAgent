using Philips.IBE.IBEAgent.Configuration;

namespace Philips.IBE.IBEAgent.Configuration.UnitTests;

public sealed class ContractWorkflowResolverTests
{
    private static CatalogOptions Catalog() => new()
    {
        Codecs = new Dictionary<string, CodecOptions>
        {
            ["hl7v2"] = new() { Type = "hl7v2" },
            ["json"] = new() { Type = "json" },
            ["avro-zip"] = new() { Type = "avro-zip" },
        },
        Pipelines = new Dictionary<string, IReadOnlyList<string>> { ["main"] = ["validate"] },
        Formats = new Dictionary<string, OutputFormatOptions>
        {
            ["hl7-standard"] = new() { Codec = "hl7v2", BatchCodec = "avro-zip" },
            ["json-fast"] = new() { Codec = "json" },
        },
        Workflows = new Dictionary<string, ContractWorkflowOptions>
        {
            ["adt"] = new() { Pipeline = "main", Format = "hl7-standard" },
        },
    };

    private static ContractOptions BareContract() => new()
    {
        Name = "Adt",
        Workflow = new WorkflowRef { Use = "adt" },
        Inputs = [new InputOptions { InputId = 1 }],
        Outputs = [new OutputOptions { OutputId = 100 }],
    };

    [Fact]
    public void Workflow_supplies_pipeline_and_encoding_to_bare_outputs()
    {
        var resolved = ContractWorkflowResolver.Resolve(BareContract(), Catalog());

        Assert.Equal("main", resolved.Pipeline);
        Assert.Equal("hl7v2", resolved.Outputs[0].Encoding);
        Assert.Null(resolved.Workflow);          // flattened away
        Assert.Null(resolved.Outputs[0].Format);
    }

    private static CatalogOptions ReplyOnFilterCatalog(string defaultValue) => Catalog() with
    {
        Workflows = new Dictionary<string, ContractWorkflowOptions>
        {
            ["adt"] = new()
            {
                Pipeline = "main",
                Format = "hl7-standard",
                Settings = new Dictionary<string, SettingDefinition>
                {
                    ["ReplyOnFilter"] = new() { Default = defaultValue, Allowed = ["true", "false"], Bind = "ReplyOnFilter" },
                },
            },
        },
    };

    [Fact]
    public void ReplyOnFilter_default_is_applied_from_the_workflow_setting()
    {
        // The workflow exposes a ReplyOnFilter setting defaulting to true; a bare contract inherits it.
        var resolved = ContractWorkflowResolver.Resolve(BareContract(), ReplyOnFilterCatalog("true"));

        Assert.True(resolved.ReplyOnFilter);
    }

    [Fact]
    public void ReplyOnFilter_fse_setting_overrides_the_workflow_default()
    {
        var contract = BareContract() with
        {
            Workflow = new WorkflowRef { Use = "adt", Settings = new Dictionary<string, string?> { ["ReplyOnFilter"] = "false" } },
        };

        var resolved = ContractWorkflowResolver.Resolve(contract, ReplyOnFilterCatalog("true"));

        Assert.False(resolved.ReplyOnFilter);
    }

    [Fact]
    public void ReplyOnFilter_defaults_to_false_when_no_setting_is_declared()
    {
        var resolved = ContractWorkflowResolver.Resolve(BareContract(), Catalog());   // "adt" declares no Settings

        Assert.False(resolved.ReplyOnFilter);
    }

    [Fact]
    public void Per_output_format_override_wins_over_workflow_default()
    {
        var contract = BareContract() with
        {
            Outputs =
            [
                new OutputOptions { OutputId = 100 },                        // template default -> hl7v2
                new OutputOptions { OutputId = 200, Format = "json-fast" },  // override -> json
            ],
        };

        var resolved = ContractWorkflowResolver.Resolve(contract, Catalog());

        Assert.Equal("hl7v2", resolved.Outputs[0].Encoding);
        Assert.Equal("json", resolved.Outputs[1].Encoding);
    }

    [Fact]
    public void Inline_encoding_override_wins_over_format()
    {
        var contract = BareContract() with
        {
            Outputs = [new OutputOptions { OutputId = 100, Encoding = "json" }],
        };

        var resolved = ContractWorkflowResolver.Resolve(contract, Catalog());

        Assert.Equal("json", resolved.Outputs[0].Encoding);
    }

    [Fact]
    public void Batch_codec_is_inherited_from_the_format()
    {
        var contract = BareContract() with
        {
            Outputs = [new OutputOptions { OutputId = 100, Batching = new BatchingOptions { Enabled = true } }],
        };

        var resolved = ContractWorkflowResolver.Resolve(contract, Catalog());

        Assert.Equal("avro-zip", resolved.Outputs[0].Batching!.Codec);
    }

    [Fact]
    public void Inline_batch_codec_override_wins_over_format()
    {
        var contract = BareContract() with
        {
            Outputs =
            [
                new OutputOptions { OutputId = 100, Batching = new BatchingOptions { Enabled = true, Codec = "hl7v2" } },
            ],
        };

        var resolved = ContractWorkflowResolver.Resolve(contract, Catalog());

        Assert.Equal("hl7v2", resolved.Outputs[0].Batching!.Codec);
    }

    [Fact]
    public void Manual_contract_without_workflow_preserves_inline_pipeline_and_encoding()
    {
        var contract = new ContractOptions
        {
            Name = "Manual",
            Pipeline = "main",
            Inputs = [new InputOptions { InputId = 1 }],
            Outputs = [new OutputOptions { OutputId = 100, Encoding = "hl7v2" }],
        };

        var resolved = ContractWorkflowResolver.Resolve(contract, Catalog());

        Assert.Equal("main", resolved.Pipeline);
        Assert.Equal("hl7v2", resolved.Outputs[0].Encoding);
    }

    [Fact]
    public void Message_level_settings_pass_through_untouched()
    {
        var contract = BareContract() with
        {
            Acknowledgement = new AckOptions { IsEnabled = false },
            Outputs =
            [
                new OutputOptions
                {
                    OutputId = 100,
                    DeliveryGuarantee = Abstractions.DeliveryGuarantee.AtLeastOnce,
                    Retry = new RetryOptions { MaxAttempts = 7 },
                },
            ],
        };

        var resolved = ContractWorkflowResolver.Resolve(contract, Catalog());

        Assert.False(resolved.Acknowledgement.IsEnabled);
        Assert.Equal(Abstractions.DeliveryGuarantee.AtLeastOnce, resolved.Outputs[0].DeliveryGuarantee);
        Assert.Equal(7, resolved.Outputs[0].Retry.MaxAttempts);
    }

    [Fact]
    public void Unknown_workflow_throws()
    {
        var contract = BareContract() with { Workflow = new WorkflowRef { Use = "missing" } };

        var ex = Assert.Throws<ContractResolutionException>(() => ContractWorkflowResolver.Resolve(contract, Catalog()));

        Assert.Contains("unknown Workflow", ex.Message);
    }

    [Fact]
    public void Unknown_output_format_throws()
    {
        var contract = BareContract() with
        {
            Outputs = [new OutputOptions { OutputId = 100, Format = "missing" }],
        };

        var ex = Assert.Throws<ContractResolutionException>(() => ContractWorkflowResolver.Resolve(contract, Catalog()));

        Assert.Contains("unknown Format", ex.Message);
    }

    private static CatalogOptions MultiFormatCatalog(params string[] formats) => Catalog() with
    {
        Workflows = new Dictionary<string, ContractWorkflowOptions>
        {
            ["adt"] = new() { Pipeline = "main", Formats = formats },
        },
    };

    [Fact]
    public void Multi_format_workflow_binds_each_output_to_its_chosen_member()
    {
        var contract = BareContract() with
        {
            Outputs =
            [
                new OutputOptions { OutputId = 100, Format = "hl7-standard" },
                new OutputOptions { OutputId = 200, Format = "json-fast" },
            ],
        };

        var resolved = ContractWorkflowResolver.Resolve(contract, MultiFormatCatalog("hl7-standard", "json-fast"));

        Assert.Equal("hl7v2", resolved.Outputs[0].Encoding);
        Assert.Equal("json", resolved.Outputs[1].Encoding);
    }

    [Fact]
    public void Output_format_outside_the_declared_set_throws()
    {
        var contract = BareContract() with
        {
            Outputs = [new OutputOptions { OutputId = 100, Format = "json-fast" }],
        };

        var ex = Assert.Throws<ContractResolutionException>(
            () => ContractWorkflowResolver.Resolve(contract, MultiFormatCatalog("hl7-standard")));

        Assert.Contains("not one of the workflow's declared Formats", ex.Message);
    }

    [Fact]
    public void Output_without_a_format_falls_back_to_the_first_declared_and_notes_it()
    {
        var contract = BareContract() with
        {
            Outputs = [new OutputOptions { OutputId = 100 }],   // no Format chosen
        };
        var notes = new List<string>();

        var resolved = ContractWorkflowResolver.Resolve(contract, MultiFormatCatalog("hl7-standard", "json-fast"), notes.Add);

        Assert.Equal("hl7v2", resolved.Outputs[0].Encoding);   // first declared (hl7-standard) -> hl7v2
        Assert.Single(notes);
        Assert.Contains("defaulting to 'hl7-standard'", notes[0]);
    }

    [Fact]
    public void Single_declared_format_inherits_without_a_note()
    {
        var notes = new List<string>();

        var resolved = ContractWorkflowResolver.Resolve(BareContract(), MultiFormatCatalog("hl7-standard"), notes.Add);

        Assert.Equal("hl7v2", resolved.Outputs[0].Encoding);
        Assert.Empty(notes);
    }

    private static CatalogOptions SettingsCatalog() => Catalog() with
    {
        Workflows = new Dictionary<string, ContractWorkflowOptions>
        {
            ["adt"] = new()
            {
                Pipeline = "main",
                Format = "hl7-standard",
                Settings = new Dictionary<string, SettingDefinition>
                {
                    ["AckTimeoutSeconds"] = new() { Default = "30", Min = 5, Max = 60, Bind = "Acknowledgement.TimeoutMs", Scale = 1000 },
                    ["MaxRetries"] = new() { Default = "3", Min = 1, Max = 5, Bind = "Outputs[].Retry.MaxAttempts" },
                },
            },
        },
    };

    private static ContractOptions SettingsContract(params (string Key, string Value)[] settings) => BareContract() with
    {
        Workflow = new WorkflowRef
        {
            Use = "adt",
            Settings = settings.ToDictionary(s => s.Key, s => (string?)s.Value),
        },
        Outputs = [new OutputOptions { OutputId = 100 }, new OutputOptions { OutputId = 200 }],
    };

    [Fact]
    public void Setting_binds_and_scales_onto_a_contract_field()
    {
        var resolved = ContractWorkflowResolver.Resolve(SettingsContract(("AckTimeoutSeconds", "45")), SettingsCatalog());

        Assert.Equal(45000, resolved.Acknowledgement.TimeoutMs);   // 45 seconds * 1000 (Scale)
    }

    [Fact]
    public void Setting_default_is_applied_when_the_fse_omits_it()
    {
        var resolved = ContractWorkflowResolver.Resolve(SettingsContract(), SettingsCatalog());

        Assert.Equal(30000, resolved.Acknowledgement.TimeoutMs);   // default 30 * 1000
    }

    [Fact]
    public void Setting_binds_across_every_output_via_the_wildcard()
    {
        var resolved = ContractWorkflowResolver.Resolve(SettingsContract(("MaxRetries", "5")), SettingsCatalog());

        Assert.All(resolved.Outputs, o => Assert.Equal(5, o.Retry.MaxAttempts));
    }

    [Fact]
    public void Unknown_fse_setting_throws()
    {
        var contract = BareContract() with
        {
            Workflow = new WorkflowRef { Use = "adt", Settings = new Dictionary<string, string?> { ["Nope"] = "1" } },
        };

        var ex = Assert.Throws<ContractResolutionException>(() => ContractWorkflowResolver.Resolve(contract, SettingsCatalog()));

        Assert.Contains("not exposed by workflow", ex.Message);
    }

    [Fact]
    public void Setting_out_of_range_throws()
    {
        var ex = Assert.Throws<ContractResolutionException>(
            () => ContractWorkflowResolver.Resolve(SettingsContract(("AckTimeoutSeconds", "90")), SettingsCatalog()));

        Assert.Contains("must be <= 60", ex.Message);
    }

    [Fact]
    public void Required_setting_without_a_default_throws_when_missing()
    {
        var catalog = Catalog() with
        {
            Workflows = new Dictionary<string, ContractWorkflowOptions>
            {
                ["adt"] = new()
                {
                    Pipeline = "main",
                    Format = "hl7-standard",
                    Settings = new Dictionary<string, SettingDefinition> { ["MustSet"] = new() { Description = "A required knob." } },
                },
            },
        };

        var ex = Assert.Throws<ContractResolutionException>(() => ContractWorkflowResolver.Resolve(BareContract(), catalog));

        Assert.Contains("is required", ex.Message);
    }

    private static ResourceResolver ResourceContext(Func<string, string?>? secrets = null) =>
        new(Path.Combine(Path.GetTempPath(), "ibe-resources"),
            new Dictionary<string, ResourceDefinition>
            {
                ["adt-default-rules"] = new() { Ref = "adt-default.rules.json", ContentType = "application/vnd.ibe.filter-rules+json" },
            },
            secrets);

    private static CatalogOptions FileSettingCatalog() => Catalog() with
    {
        Workflows = new Dictionary<string, ContractWorkflowOptions>
        {
            ["adt"] = new()
            {
                Pipeline = "main",
                Format = "hl7-standard",
                Settings = new Dictionary<string, SettingDefinition>
                {
                    ["FilterRules"] = new()
                    {
                        Kind = "file",
                        ContentType = "application/vnd.ibe.filter-rules+json",
                        Default = "adt-default-rules",
                        Bind = "stage:hl7-filter.Ruleset",
                    },
                },
            },
        },
    };

    [Fact]
    public void File_setting_resolves_into_a_stage_parameter_and_the_manifest()
    {
        var resources = ResourceContext();

        var resolved = ContractWorkflowResolver.Resolve(BareContract(), FileSettingCatalog(), resources: resources);

        var path = resolved.StageParameterSets!["hl7-filter"].Get("Ruleset");
        Assert.EndsWith("adt-default.rules.json", path);
        Assert.Contains(resources.Manifest, r => r.Setting == "FilterRules" && r.Path == path);
    }

    [Fact]
    public void File_setting_rejects_path_traversal()
    {
        var contract = BareContract() with
        {
            Workflow = new WorkflowRef { Use = "adt", Settings = new Dictionary<string, string?> { ["FilterRules"] = "../../secrets/evil.json" } },
        };

        var ex = Assert.Throws<ContractResolutionException>(
            () => ContractWorkflowResolver.Resolve(contract, FileSettingCatalog(), resources: ResourceContext()));

        Assert.Contains("escapes the allowed resources root", ex.Message);
    }

    [Fact]
    public void File_setting_rejects_absolute_paths()
    {
        var contract = BareContract() with
        {
            Workflow = new WorkflowRef { Use = "adt", Settings = new Dictionary<string, string?> { ["FilterRules"] = Path.Combine(Path.GetTempPath(), "evil.json") } },
        };

        var ex = Assert.Throws<ContractResolutionException>(
            () => ContractWorkflowResolver.Resolve(contract, FileSettingCatalog(), resources: ResourceContext()));

        Assert.Contains("must be a path relative to the resources root", ex.Message);
    }

    [Fact]
    public void File_setting_without_a_resources_context_throws()
    {
        var ex = Assert.Throws<ContractResolutionException>(
            () => ContractWorkflowResolver.Resolve(BareContract(), FileSettingCatalog()));

        Assert.Contains("needs a resources context", ex.Message);
    }

    [Fact]
    public void Secret_setting_resolves_and_is_absent_from_the_manifest()
    {
        var catalog = Catalog() with
        {
            Workflows = new Dictionary<string, ContractWorkflowOptions>
            {
                ["adt"] = new()
                {
                    Pipeline = "main",
                    Format = "hl7-standard",
                    Settings = new Dictionary<string, SettingDefinition>
                    {
                        ["ApiKey"] = new() { Kind = "secret", Default = "downstream-key", Bind = "stage:hl7-filter.ApiKey" },
                    },
                },
            },
        };
        var resources = ResourceContext(secrets: name => name == "downstream-key" ? "s3cr3t" : null);

        var resolved = ContractWorkflowResolver.Resolve(BareContract(), catalog, resources: resources);

        Assert.Equal("s3cr3t", resolved.StageParameterSets!["hl7-filter"].Get("ApiKey"));
        Assert.Empty(resources.Manifest);
    }

    [Fact]
    public void File_setting_accepts_a_direct_relative_path_without_a_declared_resource()
    {
        var resources = new ResourceResolver(Path.Combine(Path.GetTempPath(), "ibe-resources"));   // no Resources entries declared
        var contract = BareContract() with
        {
            Workflow = new WorkflowRef { Use = "adt", Settings = new Dictionary<string, string?> { ["FilterRules"] = "site-a/adt.rules.json" } },
        };

        var resolved = ContractWorkflowResolver.Resolve(contract, FileSettingCatalog(), resources: resources);

        var path = resolved.StageParameterSets!["hl7-filter"].Get("Ruleset");
        Assert.EndsWith(Path.Combine("site-a", "adt.rules.json"), path);
    }
}
