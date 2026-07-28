using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Persistence;

// §3.9 — out-of-process replay target: wraps a bare IOutboundEndpoint (no queue/dispatcher; the
// ForwardWorker already serializes replays one at a time) and resolves/re-parks directly against
// the SAME IForwardStore row the in-process DeliveryLeg would have used. Used only by the
// out-of-process ForwardService host; the in-process host reuses the compiled DeliveryLeg instead.
public sealed class EndpointReplayTarget : IReplayTarget
{
    private readonly int _outputId;
    private readonly IOutboundEndpoint _endpoint;
    private readonly IForwardStore _store;

    public EndpointReplayTarget(int outputId, IOutboundEndpoint endpoint, IForwardStore store)
    {
        _outputId = outputId;
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async ValueTask ReplayAsync(MessageContext context, CancellationToken cancellationToken)
    {
        context.MarkReplay();
        var result = await _endpoint.SendAsync(context, cancellationToken);

        if (result.Outcome == DeliveryOutcome.Delivered)
            await _store.ResolveAsync(context, _outputId, cancellationToken);
        else
            throw new InvalidOperationException(result.Error ?? "delivery failed"); // ForwardWorker reschedules/parks
    }
}
