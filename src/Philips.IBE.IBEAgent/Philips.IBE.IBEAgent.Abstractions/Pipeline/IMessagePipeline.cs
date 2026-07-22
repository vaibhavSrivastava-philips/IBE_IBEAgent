namespace Philips.IBE.IBEAgent.Abstractions;

// the SHARED pipeline, run once per message before fan-out.
public interface IMessagePipeline
{
    Task<PipelineResult> ExecuteAsync(MessageContext context);
}