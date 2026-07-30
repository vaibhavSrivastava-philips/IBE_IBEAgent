namespace Philips.IBE.IBEAgent.Abstractions;

// §3.6 — a shared-pipeline stage: process the message (parse/validate/filter/enrich/transform) and
// RETURN whether the pipeline continues or the message is filtered (StageResult). The pipeline drives
// iteration, so a stage cannot forget to "continue" and silently skip the rest — it returns a result
// (Continue / Filter) or throws. Stages mutate the shared MessageContext (headers/payload) directly.
public interface IMessageStage
{
    Task<StageResult> ProcessAsync(MessageContext context);
}