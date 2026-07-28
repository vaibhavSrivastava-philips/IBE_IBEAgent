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
            Pipelines = new Dictionary<string, IReadOnlyList<object>>
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
            Pipelines = new Dictionary<string, IReadOnlyList<object>>
            {
                ["main"] = ["validate", "transform"],
            },
        };

        var result = CatalogOptionsValidator.Validate(catalog);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Parallel_stage_with_single_branch_fails()
    {
        var catalog = new CatalogOptions
        {
            Pipelines = new Dictionary<string, IReadOnlyList<object>>
            {
                ["main"] = [new ParallelStageOptions { Branches = [["a"]] }],
            },
        };

        var result = CatalogOptionsValidator.Validate(catalog);

        Assert.Contains(result.Errors, e => e.Contains("at least two branches"));
    }

    [Fact]
    public void Parallel_stage_with_two_branches_passes()
    {
        var catalog = new CatalogOptions
        {
            Pipelines = new Dictionary<string, IReadOnlyList<object>>
            {
                ["main"] = [new ParallelStageOptions { Branches = [["a"], ["b"]] }],
            },
        };

        var result = CatalogOptionsValidator.Validate(catalog);

        Assert.True(result.IsValid);
    }
}
