using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;

namespace Philips.IBE.IBEAgent.Core;

// §3.10/§3.6a — compiles a named Catalog pipeline (list of stage-name tokens, optionally including a
// nested `parallel` composite) into a real IMessagePipeline, resolving each stage name through the
// ComponentRegistry. Pure name -> instance wiring; no processing logic lives here.
public static class PipelineBuilder
{
    public static IMessagePipeline Build(string? pipelineName, CatalogOptions catalog, ComponentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(registry);

        if (string.IsNullOrWhiteSpace(pipelineName))
            return new MessagePipeline([]);   // §3.6 — a contract may declare no processing stages

        if (!catalog.Pipelines.TryGetValue(pipelineName, out var tokens))
            throw new InvalidOperationException($"No catalog pipeline named '{pipelineName}'.");

        var stages = tokens.Select(token => BuildStage(token, registry)).ToList();
        return new MessagePipeline(stages);
    }

    private static IMessageStage BuildStage(object token, ComponentRegistry registry) => token switch
    {
        string name => registry.CreateStage(name),
        ParallelStageOptions parallel => new ParallelStage(
            parallel.Branches.Select(branch => (IReadOnlyList<IMessageStage>)branch
                .Select(registry.CreateStage)
                .ToList())
                .ToList()),
        _ => throw new InvalidOperationException($"Unrecognized pipeline stage token of type '{token.GetType().Name}'."),
    };
}
