using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;

namespace Philips.IBE.IBEAgent.Core;

// §3.10 — compiles a named Catalog pipeline (an ordered list of stage names) into a real
// IMessagePipeline, resolving each stage name through the ComponentRegistry. Pure name -> instance
// wiring; no processing logic lives here.
public static class PipelineBuilder
{
    public static IMessagePipeline Build(string? pipelineName, CatalogOptions catalog, ComponentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(registry);

        if (string.IsNullOrWhiteSpace(pipelineName))
            return new MessagePipeline([]);   // §3.6 — a contract may declare no processing stages

        if (!catalog.Pipelines.TryGetValue(pipelineName, out var stageNames))
            throw new InvalidOperationException($"No catalog pipeline named '{pipelineName}'.");

        var stages = stageNames.Select(registry.CreateStage).ToList();
        return new MessagePipeline(stages);
    }
}
