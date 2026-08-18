using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// §3.2 — coordinator (fresh messages only). Asks the Router for the one matching contract
// and enqueues onto that contract's ingress. Never does routing logic, processing, or
// retry replay (replay is leg-targeted, §3.9 — the Dispatcher is not a retry hub).
public sealed class Dispatcher : IMessageDispatcher
{
    private readonly IContractResolver _resolver;

    public Dispatcher(IContractResolver resolver) => _resolver = resolver;

    public Task DispatchAsync(MessageContext context, CancellationToken cancellationToken)
    {
        var runtime = _resolver.Resolve(context);
        return runtime.EnqueueAsync(context, cancellationToken).AsTask();
    }
}
