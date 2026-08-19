using Philips.IBE.IBEAgent.Configuration;

namespace Philips.IBE.IBEAgent.Configuration.UnitTests;

public sealed class CatalogOptionsValidatorTests
{
    [Fact]
    public void Empty_catalog_is_valid()
    {
        var result = CatalogOptionsValidator.Validate(new CatalogOptions());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Codec_with_blank_type_fails()
    {
        var catalog = new CatalogOptions
        {
            Codecs = new Dictionary<string, CodecOptions>
            {
                ["bad"] = new() { Type = " " },
            },
        };

        var result = CatalogOptionsValidator.Validate(catalog);

        Assert.Contains(result.Errors, e => e.Contains("non-empty Type"));
    }

    [Fact]
    public void Pipeline_with_no_stages_fails()
    {
        var catalog = new CatalogOptions
        {
            Pipelines = new Dictionary<string, IReadOnlyList<string>>
            {
                ["empty"] = [],
            },
        };

        var result = CatalogOptionsValidator.Validate(catalog);

        Assert.Contains(result.Errors, e => e.Contains("at least one stage"));
    }

    [Fact]
    public void Pipeline_with_valid_stage_names_passes()
    {
        var catalog = new CatalogOptions
        {
            Pipelines = new Dictionary<string, IReadOnlyList<string>>
            {
                ["main"] = ["validate", "transform"],
            },
        };

        var result = CatalogOptionsValidator.Validate(catalog);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Format_with_blank_codec_fails()
    {
        var catalog = new CatalogOptions
        {
            Formats = new Dictionary<string, OutputFormatOptions> { ["bad"] = new() { Codec = " " } },
        };

        var result = CatalogOptionsValidator.Validate(catalog);

        Assert.Contains(result.Errors, e => e.Contains("non-empty Codec"));
    }

    [Fact]
    public void Format_referencing_unknown_codec_fails()
    {
        var catalog = new CatalogOptions
        {
            Formats = new Dictionary<string, OutputFormatOptions> { ["fmt"] = new() { Codec = "missing" } },
        };

        var result = CatalogOptionsValidator.Validate(catalog);

        Assert.Contains(result.Errors, e => e.Contains("unknown Codec"));
    }

    [Fact]
    public void Format_referencing_unknown_batch_codec_fails()
    {
        var catalog = new CatalogOptions
        {
            Codecs = new Dictionary<string, CodecOptions> { ["hl7v2"] = new() { Type = "hl7v2" } },
            Formats = new Dictionary<string, OutputFormatOptions> { ["fmt"] = new() { Codec = "hl7v2", BatchCodec = "missing" } },
        };

        var result = CatalogOptionsValidator.Validate(catalog);

        Assert.Contains(result.Errors, e => e.Contains("unknown BatchCodec"));
    }

    [Fact]
    public void Workflow_referencing_unknown_pipeline_fails()
    {
        var catalog = new CatalogOptions
        {
            Workflows = new Dictionary<string, ContractWorkflowOptions> { ["t"] = new() { Pipeline = "missing" } },
        };

        var result = CatalogOptionsValidator.Validate(catalog);

        Assert.Contains(result.Errors, e => e.Contains("unknown Pipeline"));
    }

    [Fact]
    public void Workflow_referencing_unknown_format_fails()
    {
        var catalog = new CatalogOptions
        {
            Workflows = new Dictionary<string, ContractWorkflowOptions> { ["t"] = new() { Format = "missing" } },
        };

        var result = CatalogOptionsValidator.Validate(catalog);

        Assert.Contains(result.Errors, e => e.Contains("unknown Format"));
    }

    [Fact]
    public void Valid_formats_and_workflows_pass()
    {
        var catalog = new CatalogOptions
        {
            Codecs = new Dictionary<string, CodecOptions> { ["hl7v2"] = new() { Type = "hl7v2" } },
            Pipelines = new Dictionary<string, IReadOnlyList<string>> { ["main"] = ["validate"] },
            Formats = new Dictionary<string, OutputFormatOptions> { ["hl7-standard"] = new() { Codec = "hl7v2" } },
            Workflows = new Dictionary<string, ContractWorkflowOptions> { ["adt"] = new() { Pipeline = "main", Format = "hl7-standard" } },
        };

        var result = CatalogOptionsValidator.Validate(catalog);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Workflow_declaring_both_format_and_formats_fails()
    {
        var catalog = new CatalogOptions
        {
            Codecs = new Dictionary<string, CodecOptions> { ["hl7v2"] = new() { Type = "hl7v2" } },
            Formats = new Dictionary<string, OutputFormatOptions> { ["hl7-standard"] = new() { Codec = "hl7v2" } },
            Workflows = new Dictionary<string, ContractWorkflowOptions>
            {
                ["t"] = new() { Format = "hl7-standard", Formats = ["hl7-standard"] },
            },
        };

        var result = CatalogOptionsValidator.Validate(catalog);

        Assert.Contains(result.Errors, e => e.Contains("both Format and Formats"));
    }

    [Fact]
    public void Workflow_with_unknown_formats_entry_fails()
    {
        var catalog = new CatalogOptions
        {
            Workflows = new Dictionary<string, ContractWorkflowOptions> { ["t"] = new() { Formats = ["missing"] } },
        };

        var result = CatalogOptionsValidator.Validate(catalog);

        Assert.Contains(result.Errors, e => e.Contains("unknown Format 'missing' in Formats"));
    }
}
