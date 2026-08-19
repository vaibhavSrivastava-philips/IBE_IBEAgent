using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class ContractCompilerTests
{
    private static CatalogOptions ValidCatalog() => new()
    {
        Pipelines = new Dictionary<string, IReadOnlyList<string>> { ["main"] = ["stamp"] },
        Codecs = new Dictionary<string, CodecOptions>
        {
            ["hl7v2"] = new() { Type = "hl7v2" },
        },
    };

    private static ComponentRegistry ValidRegistry(FakeOutboundEndpoint endpoint) => new ComponentRegistry()
        .RegisterStage("stamp", _ => new FakeStage())
        .RegisterMessageCodec("hl7v2", _ => new FakeMessageCodec())
        .RegisterOutboundEndpoint(100, _ => endpoint);

    private static ContractOptions ValidContract() => new()
    {
        Name = "Adt",
        Inputs = [new InputOptions { InputId = 1 }],
        Pipeline = "main",
        Outputs = [new OutputOptions { OutputId = 100, Encoding = "hl7v2" }],
    };

    [Fact]
    public void Compile_builds_a_runnable_contract_runtime()
    {
        var endpoint = new FakeOutboundEndpoint();
        var compiler = new ContractCompiler(ValidCatalog(), ValidRegistry(endpoint));

        var runtime = compiler.Compile(ValidContract());

        Assert.NotNull(runtime);
    }

    [Fact]
    public void Compile_throws_with_batched_errors_when_contract_is_invalid()
    {
        var endpoint = new FakeOutboundEndpoint();
        var compiler = new ContractCompiler(ValidCatalog(), ValidRegistry(endpoint));
        var contract = ValidContract() with { Inputs = [] };

        var ex = Assert.Throws<ContractCompilationException>(() => compiler.Compile(contract));

        Assert.Contains(ex.Errors, e => e.Contains("at least one input"));
    }

    [Fact]
    public void Compile_throws_when_pipeline_name_is_unknown()
    {
        var endpoint = new FakeOutboundEndpoint();
        var compiler = new ContractCompiler(ValidCatalog(), ValidRegistry(endpoint));
        var contract = ValidContract() with { Pipeline = "missing" };

        var ex = Assert.Throws<ContractCompilationException>(() => compiler.Compile(contract));

        Assert.Contains(ex.Errors, e => e.Contains("unknown Pipeline"));
    }

    private sealed class FakeStage : IMessageStage
    {
        public Task<StageResult> ProcessAsync(MessageContext context) => Task.FromResult(StageResult.Continue);
    }
}
