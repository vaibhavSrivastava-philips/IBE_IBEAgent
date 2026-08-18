namespace Philips.IBE.IBEAgent.Abstractions;

// §3.9 — the leg-targeted replay seam. Implemented by DeliveryLeg (Core); consumed by the
// ForwardWorker (Persistence) without a reference to Core, so both in-process and
// out-of-process hosting modes can share the same worker implementation.
public interface IReplayTarget
{
    ValueTask ReplayAsync(MessageContext context, CancellationToken cancellationToken);
}
