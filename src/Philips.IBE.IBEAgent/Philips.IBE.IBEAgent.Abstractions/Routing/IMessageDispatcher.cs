namespace Philips.IBE.IBEAgent.Abstractions;

public interface IMessageDispatcher        // coordinator (fresh messages only; never retry replay).
{
    Task DispatchAsync(MessageContext context, CancellationToken cancellationToken);
}