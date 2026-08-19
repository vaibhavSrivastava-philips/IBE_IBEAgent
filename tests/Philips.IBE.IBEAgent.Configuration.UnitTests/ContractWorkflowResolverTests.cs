using Philips.IBE.IBEAgent.Configuration;

namespace Philips.IBE.IBEAgent.Configuration.UnitTests;

public sealed class ContractTemplateResolverTests
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
        Templates = new Dictionary<string, ContractTemplateOptions>
        {
            ["adt"] = new() { Pipeline = "main", Format = "hl7-standard", ReplyOnFilter = true },
        },
    };

    private static ContractOptions BareContract() => new()
    {
        Name = "Adt",
        Template = "adt",
        Inputs = [new InputOptions { InputId = 1 }],
        Outputs = [new OutputOptions { OutputId = 100 }],
    };

    [Fact]
    public void Template_supplies_pipeline_and_encoding_to_bare_outputs()
    {
        var resolved = ContractTemplateResolver.Resolve(BareContract(), Catalog());

        Assert.Equal("main", resolved.Pipeline);
        Assert.Equal("hl7v2", resolved.Outputs[0].Encoding);
        Assert.Null(resolved.Template);          // flattened away
        Assert.Null(resolved.Outputs[0].Format);
    }

    [Fact]
    public void ReplyOnFilter_is_inherited_from_the_template()
    {
        // Template "adt" sets ReplyOnFilter = true (the developer default); a bare contract inherits it.
        var resolved = ContractTemplateResolver.Resolve(BareContract(), Catalog());

        Assert.True(resolved.ReplyOnFilter);
    }

    [Fact]
    public void Contract_ReplyOnFilter_override_wins_over_the_template()
    {
        var resolved = ContractTemplateResolver.Resolve(BareContract() with { ReplyOnFilter = false }, Catalog());

        Assert.False(resolved.ReplyOnFilter);
    }

    [Fact]
    public void ReplyOnFilter_defaults_to_false_when_unset_by_template_and_contract()
    {
        var catalog = Catalog() with
        {
            Templates = new Dictionary<string, ContractTemplateOptions> { ["adt"] = new() { Format = "hl7-standard" } },
        };

        var resolved = ContractTemplateResolver.Resolve(BareContract(), catalog);

        Assert.False(resolved.ReplyOnFilter);
    }

    [Fact]
    public void Per_output_format_override_wins_over_template_default()
    {
        var contract = BareContract() with
        {
            Outputs =
            [
                new OutputOptions { OutputId = 100 },                        // template default -> hl7v2
                new OutputOptions { OutputId = 200, Format = "json-fast" },  // override -> json
            ],
        };

        var resolved = ContractTemplateResolver.Resolve(contract, Catalog());

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

        var resolved = ContractTemplateResolver.Resolve(contract, Catalog());

        Assert.Equal("json", resolved.Outputs[0].Encoding);
    }

    [Fact]
    public void Batch_codec_is_inherited_from_the_format()
    {
        var contract = BareContract() with
        {
            Outputs = [new OutputOptions { OutputId = 100, Batching = new BatchingOptions { Enabled = true } }],
        };

        var resolved = ContractTemplateResolver.Resolve(contract, Catalog());

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

        var resolved = ContractTemplateResolver.Resolve(contract, Catalog());

        Assert.Equal("hl7v2", resolved.Outputs[0].Batching!.Codec);
    }

    [Fact]
    public void Manual_contract_without_template_preserves_inline_pipeline_and_encoding()
    {
        var contract = new ContractOptions
        {
            Name = "Manual",
            Pipeline = "main",
            Inputs = [new InputOptions { InputId = 1 }],
            Outputs = [new OutputOptions { OutputId = 100, Encoding = "hl7v2" }],
        };

        var resolved = ContractTemplateResolver.Resolve(contract, Catalog());

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

        var resolved = ContractTemplateResolver.Resolve(contract, Catalog());

        Assert.False(resolved.Acknowledgement.IsEnabled);
        Assert.Equal(Abstractions.DeliveryGuarantee.AtLeastOnce, resolved.Outputs[0].DeliveryGuarantee);
        Assert.Equal(7, resolved.Outputs[0].Retry.MaxAttempts);
    }

    [Fact]
    public void Unknown_template_throws()
    {
        var contract = BareContract() with { Template = "missing" };

        var ex = Assert.Throws<ContractResolutionException>(() => ContractTemplateResolver.Resolve(contract, Catalog()));

        Assert.Contains("unknown Template", ex.Message);
    }

    [Fact]
    public void Unknown_output_format_throws()
    {
        var contract = BareContract() with
        {
            Outputs = [new OutputOptions { OutputId = 100, Format = "missing" }],
        };

        var ex = Assert.Throws<ContractResolutionException>(() => ContractTemplateResolver.Resolve(contract, Catalog()));

        Assert.Contains("unknown Format", ex.Message);
    }
}
