using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Persistence;

// §3.9 — out-of-process replay target: wraps a bare IOutboundEndpoint (no queue/dispatcher; the
// ForwardWorker serializes replays one at a time). Delivers straight through and throws on failure;
// the ForwardWorker owns resolve-on-success / reschedule / park (keyed by the store entry id). Used
// only by the out-of-process ForwardService host; the in-process host reuses the compiled DeliveryLeg.
public sealed class EndpointReplayTarget : IReplayTarget, IAsyncDisposable
{
    private readonly IOutboundEndpoint _endpoint;

    public EndpointReplayTarget(IOutboundEndpoint endpoint)
        => _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));

    public async ValueTask ReplayAsync(MessageContext context, CancellationToken cancellationToken)
    {
        context.MarkReplay();
        var result = await _endpoint.SendAsync(context, cancellationToken);
        if (result.Outcome != DeliveryOutcome.Delivered)
            throw new InvalidOperationException(result.Error ?? "delivery failed"); // worker reschedules/parks; resolve-on-success is the worker's job
    }

    public async ValueTask DisposeAsync()
    {
        if (_endpoint is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else if (_endpoint is IDisposable disposable)
            disposable.Dispose();
    }
}
