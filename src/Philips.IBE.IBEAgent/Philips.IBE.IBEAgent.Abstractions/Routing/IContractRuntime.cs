namespace Philips.IBE.IBEAgent.Abstractions;

public interface IContractRuntime          // ingress sink + shared reception + fan-out.
{
    ValueTask EnqueueAsync(MessageContext context, CancellationToken cancellationToken);
    Task RunAsync(CancellationToken cancellationToken);
    Task DrainAsync(TimeSpan timeout);
}