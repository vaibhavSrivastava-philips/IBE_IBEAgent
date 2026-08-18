using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Persistence;

// §3.10 — trivial dictionary-backed implementation, populated at composition time from the
// compiled legs (each DeliveryLeg is itself an IReplayTarget).
public sealed class ReplayTargetRegistry : IReplayTargetRegistry, IAsyncDisposable
{
    private readonly IReadOnlyDictionary<int, IReplayTarget> _targets;

    public ReplayTargetRegistry(IEnumerable<KeyValuePair<int, IReplayTarget>> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        _targets = targets.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    public bool TryGet(int outputId, out IReplayTarget? target)
        => _targets.TryGetValue(outputId, out target);

    public async ValueTask DisposeAsync()
    {
        var disposed = new HashSet<IReplayTarget>(ReferenceEqualityComparer<IReplayTarget>.Instance);
        foreach (var target in _targets.Values)
        {
            if (!disposed.Add(target))
                continue;

            if (target is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else if (target is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static ReferenceEqualityComparer<T> Instance { get; } = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
