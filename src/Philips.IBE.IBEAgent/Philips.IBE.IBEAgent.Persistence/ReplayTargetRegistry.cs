using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Persistence;

// §3.10 — trivial dictionary-backed implementation, populated at composition time from the
// compiled legs (each DeliveryLeg is itself an IReplayTarget).
public sealed class ReplayTargetRegistry : IReplayTargetRegistry
{
    private readonly IReadOnlyDictionary<int, IReplayTarget> _targets;

    public ReplayTargetRegistry(IEnumerable<KeyValuePair<int, IReplayTarget>> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        _targets = targets.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    public bool TryGet(int outputId, out IReplayTarget? target)
        => _targets.TryGetValue(outputId, out target);
}
