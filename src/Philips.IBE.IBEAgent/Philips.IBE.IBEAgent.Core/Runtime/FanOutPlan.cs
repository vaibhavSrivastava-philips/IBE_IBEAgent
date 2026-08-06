using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Telemetry;

namespace Philips.IBE.IBEAgent.Core;

// §4 — the fan-out set for ONE source input. For source-only routing the applicable legs + required
// count depend ONLY on the source id (a fixed compile-time set), so the plan is built ONCE per
// (contract, input): no per-message LINQ, and the single-leg vs multi-leg shape is resolved at
// construction. When any applicable leg carries a RouteWhen content filter the applicable set depends
// on the message, so a RoutedFanOutPlan resolves it per message — pay-only-if-used: everyone else keeps
// the precomputed fast path.
internal abstract class FanOutPlan
{
    // REQUIRED applicable-leg count for static (source-only) plans; RoutedFanOutPlan recomputes per message.
    protected int RequiredCount { get; }

    protected FanOutPlan(int requiredCount) => RequiredCount = requiredCount;

    // Picks the concrete plan for a source's applicable legs (in declaration order). Chosen once at
    // compile time; a one-leg source gets the zero-allocation SingleLegPlan.
    public static FanOutPlan For(DeliveryLeg[] applicableLegs)
    {
        ArgumentNullException.ThrowIfNull(applicableLegs);

        // Content-routed: at least one applicable leg filters on message content -> resolve per message.
        if (Array.Exists(applicableLegs, leg => leg.HasRouteWhen))
        {
            return new RoutedFanOutPlan(
                Array.FindAll(applicableLegs, leg => !leg.HasRouteWhen),
                Array.FindAll(applicableLegs, leg => leg.HasRouteWhen));
        }

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

    // Arm the reply (Normal ack fires "received" here) with the applicable required count, then enqueue.
    public abstract ValueTask DispatchAsync(MessageContext context, CancellationToken cancellationToken);
}

// The dominant (~80%) high-fidelity shape: exactly one applicable leg. Reuses THIS envelope in place
// (no clone, no Task.WhenAll) — a truly allocation-free per-message fan-out.
internal sealed class SingleLegPlan : FanOutPlan
{
    private readonly DeliveryLeg _leg;

    public SingleLegPlan(DeliveryLeg leg, int requiredCount) : base(requiredCount) => _leg = leg;

    public override ValueTask DispatchAsync(MessageContext context, CancellationToken cancellationToken)
    {
        context.Reply.OnFannedOut(RequiredCount);
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

    public override async ValueTask DispatchAsync(MessageContext context, CancellationToken cancellationToken)
    {
        context.Reply.OnFannedOut(RequiredCount);
        if (_legs.Length == 0)
            return;   // no applicable leg — reply is already armed with 0 required

        var enqueues = new Task[_legs.Length];
        for (var i = 0; i < _legs.Length; i++)
            enqueues[i] = _legs[i].EnqueueAsync(context.CloneForLeg(_legs[i].OutputId), cancellationToken).AsTask();

        await Task.WhenAll(enqueues);
    }
}

// Content-routed fan-out: unconditional legs (no RouteWhen) always apply; conditional legs apply only
// when their RouteWhen matches the message Headers. The applicable set + required count are therefore
// per message. A message matching NO leg is a filtered drop (observable) — not a silent success, which
// would let an enhanced ack report "delivered nothing"; an FSE adds a catch-all (RouteWhen-less) output
// when guaranteed delivery is required.
internal sealed class RoutedFanOutPlan : FanOutPlan
{
    private readonly DeliveryLeg[] _unconditional;
    private readonly DeliveryLeg[] _conditional;

    public RoutedFanOutPlan(DeliveryLeg[] unconditional, DeliveryLeg[] conditional)
        : base(requiredCount: 0)   // required count is per-message; the base value is unused
    {
        _unconditional = unconditional;
        _conditional = conditional;
    }

    public override async ValueTask DispatchAsync(MessageContext context, CancellationToken cancellationToken)
    {
        var applicable = new List<DeliveryLeg>(_unconditional.Length + _conditional.Length);
        applicable.AddRange(_unconditional);
        foreach (var leg in _conditional)
        {
            if (leg.AcceptsMessage(context.Headers))
                applicable.Add(leg);
        }

        if (applicable.Count == 0)
        {
            AgentDiagnostics.FilteredMessages.Add(1,
                new KeyValuePair<string, object?>("source", context.SourceEndpointId),
                new KeyValuePair<string, object?>("reason", "no route matched"));
            context.Reply.ReportFiltered("no route matched");
            return;
        }

        var required = 0;
        foreach (var leg in applicable)
        {
            if (leg.Required)
                required++;
        }
        context.Reply.OnFannedOut(required);

        if (applicable.Count == 1)
        {
            context.SetLeg(applicable[0].OutputId);
            await applicable[0].EnqueueAsync(context, cancellationToken);
            return;
        }

        var enqueues = new Task[applicable.Count];
        for (var i = 0; i < applicable.Count; i++)
            enqueues[i] = applicable[i].EnqueueAsync(context.CloneForLeg(applicable[i].OutputId), cancellationToken).AsTask();

        await Task.WhenAll(enqueues);
    }
}
