using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class CoreComponentRegistrationsTests
{
    [Fact]
    public void AddCoreStages_registers_the_passthrough_stage()
    {
        var registry = new ComponentRegistry().AddCoreStages();

        var stage = registry.CreateStage(PassThroughStage.Name, StageParameters.None);

        Assert.IsType<PassThroughStage>(stage);
    }

    [Fact]
    public async Task A_named_pipeline_of_core_stages_compiles_and_runs()
    {
        // Regression guard: a non-empty catalog pipeline naming a registered stage must NOT throw
        // (before AddCoreStages existed, CreateStage threw for every stage name).
        var registry = new ComponentRegistry().AddCoreStages();
        var catalog = new CatalogOptions
        {
            Pipelines = new Dictionary<string, IReadOnlyList<string>> { ["main"] = [PassThroughStage.Name] },
        };

        var pipeline = PipelineBuilder.Build("main", catalog, registry);
        var result = await pipeline.ExecuteAsync(MessageContextBuilder.Create());

        Assert.False(result.ShortCircuited);
    }
}
