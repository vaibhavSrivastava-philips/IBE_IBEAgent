namespace Philips.IBE.IBEAgent.Abstractions;

// §3.9 — OutputId -> IReplayTarget lookup used by the ForwardWorker. Populated at composition
// time from the compiled legs; an OutputId that no longer resolves means config drift (parked,
// never fatal, per §3.9 edge cases).
public interface IReplayTargetRegistry
{
    bool TryGet(int outputId, out IReplayTarget? target);
}
