namespace Philips.IBE.IBEAgent.Abstractions;

// the SHARED pipeline, run once per message before fan-out. Returns a ValueTask so the common
// no-stage (high-fidelity) and all-synchronous-stage cases complete without allocating a Task.
public interface IMessagePipeline
{
    ValueTask<PipelineResult> ExecuteAsync(MessageContext context);
}