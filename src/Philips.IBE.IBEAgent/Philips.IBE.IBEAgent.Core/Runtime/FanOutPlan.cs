using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// §4 — the precomputed fan-out set for ONE source input. Which legs accept a given source, and how
// many of them are required, depend ONLY on the source id (a fixed compile-time set), so the plan is
// built ONCE per (contract, input) instead of being recomputed per message. This removes the
// per-message LINQ/allocation from the hot loop, and it resolves the single-leg vs multi-leg shape at
// construction (no per-message leg-count branch) — the dispatch is a single virtual call.
internal abstract class FanOutPlan
{
    // Number of REQUIRED applicable legs for this source — the reply's per-message arm count.
    public int RequiredCount { get; }

    protected FanOutPlan(int requiredCount) => RequiredCount = requiredCount;

    // Picks the concrete plan for a source's applicable legs (in declaration order). Chosen once at
    // compile time; a one-leg source gets the zero-allocation SingleLegPlan.
    public static FanOutPlan For(DeliveryLeg[] applicableLegs)
    {
        ArgumentNullException.ThrowIfNull(applicableLegs);

        var required = 0;
        foreach (var leg in applicableLegs)
        {
            if (leg.Required)
                required++;
        }

        return applicableLegs.Length == 1
            ? new SingleLegPlan(applicableLegs[0], required)
            : new MultiLegPlan(applicableLegs, required);
    }

    // Arm the reply (Normal ack fires "received" here) then enqueue to the applicable legs.
    public ValueTask DispatchAsync(MessageContext context, CancellationToken cancellationToken)
    {
        context.Reply.OnFannedOut(RequiredCount);
        return EnqueueAsync(context, cancellationToken);
    }

    protected abstract ValueTask EnqueueAsync(MessageContext context, CancellationToken cancellationToken);
}

// The dominant (~80%) high-fidelity shape: exactly one applicable leg. Reuses THIS envelope in place
// (no clone, no Task.WhenAll) — a truly allocation-free per-message fan-out.
internal sealed class SingleLegPlan : FanOutPlan
{
    private readonly DeliveryLeg _leg;

    public SingleLegPlan(DeliveryLeg leg, int requiredCount) : base(requiredCount) => _leg = leg;

    protected override ValueTask EnqueueAsync(MessageContext context, CancellationToken cancellationToken)
    {
        context.SetLeg(_leg.OutputId);
        return _leg.EnqueueAsync(context, cancellationToken);
    }
}

// Two or more (or, defensively, zero) applicable legs: each leg gets its own thin envelope clone,
// enqueued concurrently and awaited together.
internal sealed class MultiLegPlan : FanOutPlan
{
    private readonly DeliveryLeg[] _legs;

    public MultiLegPlan(DeliveryLeg[] legs, int requiredCount) : base(requiredCount) => _legs = legs;

    protected override async ValueTask EnqueueAsync(MessageContext context, CancellationToken cancellationToken)
    {
        if (_legs.Length == 0)
            return;   // no applicable leg — reply is already armed with 0 required

        var enqueues = new Task[_legs.Length];
        for (var i = 0; i < _legs.Length; i++)
            enqueues[i] = _legs[i].EnqueueAsync(context.CloneForLeg(_legs[i].OutputId), cancellationToken).AsTask();

        await Task.WhenAll(enqueues);
    }
}
