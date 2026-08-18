using Philips.IBE.IBEAgent.Configuration;

namespace Philips.IBE.IBEAgent.Configuration.UnitTests;

public sealed class ContractCatalogCrossValidatorTests
{
    private static CatalogOptions ValidCatalog() => new()
    {
        Pipelines = new Dictionary<string, IReadOnlyList<string>> { ["main"] = ["validate"] },
        Codecs = new Dictionary<string, CodecOptions>
        {
            ["hl7v2"] = new() { Type = "Hl7v2Codec" },
            ["avro-zip"] = new() { Type = "AvroZipBatchCodec" },
        },
    };

    private static ContractOptions ValidContract() => new()
    {
        Name = "Adt",
        Inputs = [new InputOptions { InputId = 1 }],
        Pipeline = "main",
        Outputs = [new OutputOptions { OutputId = 100, Encoding = "hl7v2" }],
    };

    [Fact]
    public void Valid_references_pass()
    {
        var result = ContractCatalogCrossValidator.Validate(ValidContract(), ValidCatalog());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Unknown_pipeline_fails()
    {
        var contract = ValidContract() with { Pipeline = "missing" };

        var result = ContractCatalogCrossValidator.Validate(contract, ValidCatalog());

        Assert.Contains(result.Errors, e => e.Contains("unknown Pipeline"));
    }

    [Fact]
    public void Unknown_encoding_fails()
    {
        var contract = ValidContract() with
        {
            Outputs = [new OutputOptions { OutputId = 100, Encoding = "missing" }],
        };

        var result = ContractCatalogCrossValidator.Validate(contract, ValidCatalog());

        Assert.Contains(result.Errors, e => e.Contains("unknown Encoding"));
    }

    [Fact]
    public void Unknown_batching_codec_fails()
    {
        var contract = ValidContract() with
        {
            Outputs =
            [
                new OutputOptions
                {
                    OutputId = 100,
                    Encoding = "hl7v2",
                    Batching = new BatchingOptions { Enabled = true, Codec = "missing" },
                },
            ],
        };

        var result = ContractCatalogCrossValidator.Validate(contract, ValidCatalog());

        Assert.Contains(result.Errors, e => e.Contains("unknown Batching.Codec"));
    }

    [Fact]
    public void Unresolved_encoding_fails()
    {
        var contract = ValidContract() with
        {
            Outputs = [new OutputOptions { OutputId = 100 }],   // no Encoding resolved
        };

        var result = ContractCatalogCrossValidator.Validate(contract, ValidCatalog());

        Assert.Contains(result.Errors, e => e.Contains("no resolved Encoding"));
    }

    [Fact]
    public void Batching_enabled_without_resolved_codec_fails()
    {
        var contract = ValidContract() with
        {
            Outputs =
            [
                new OutputOptions
                {
                    OutputId = 100,
                    Encoding = "hl7v2",
                    Batching = new BatchingOptions { Enabled = true },   // no Codec resolved
                },
            ],
        };

        var result = ContractCatalogCrossValidator.Validate(contract, ValidCatalog());

        Assert.Contains(result.Errors, e => e.Contains("no resolved batch Codec"));
    }
}
