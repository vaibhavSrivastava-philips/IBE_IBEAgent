namespace Philips.IBE.IBEAgent.Abstractions;

public delegate Task StageDelegate(MessageContext context);

// Chain-of-Responsibility/middleware. A stage may short-circuit, enrich headers, or replace the canonical model.
public interface IMessageStage
{
    Task InvokeAsync(MessageContext context, StageDelegate next);
}