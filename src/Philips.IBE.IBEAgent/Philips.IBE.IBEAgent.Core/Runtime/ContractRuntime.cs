using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Telemetry;

namespace Philips.IBE.IBEAgent.Core;

// One per Contract. Owns one ingress queue per input, the ONE shared pipeline, and the legs.
// Runs the shared pipeline once per message, then fans out to the applicable legs.
public sealed class ContractRuntime : IContractRuntime
{
    private readonly IReadOnlyDictionary<int, IMessageChannel> _ingressQueues; // one per input comm point
    private readonly IMessagePipeline _pipeline;                               // the ONE shared pipeline
    private readonly IReadOnlyList<DeliveryLeg> _legs;
    private readonly FrozenDictionary<int, FanOutPlan> _fanOutBySource;        // precomputed per source (§4)
    private readonly ILogger<ContractRuntime> _logger;
    private readonly List<Task> _consumers = [];

    // Exposed so the host composition root can build the ForwardWorker's IReplayTargetRegistry
    // (§3.9) without the Persistence layer needing to know about ContractRuntime internals.
    public IReadOnlyList<DeliveryLeg> Legs => _legs;

    public ContractRuntime(
        IReadOnlyDictionary<int, IMessageChannel> ingressQueues,
        IMessagePipeline pipeline,
        IReadOnlyList<DeliveryLeg> legs,
        ILogger<ContractRuntime>? logger = null)
    {
        _ingressQueues = ingressQueues;
        _pipeline = pipeline;
        _legs = legs;
        _logger = logger ?? NullLogger<ContractRuntime>.Instance;

        // The applicable legs + required count for a message are a pure function of its source id
        // (a fixed compile-time set), so resolve one fan-out plan per input ONCE here instead of
        // recomputing it per message. Every SourceEndpointId that reaches ConsumeAsync is an ingress
        // key (EnqueueAsync routes by it), so this map is total over the sources we can observe.
        _fanOutBySource = ingressQueues.Keys.ToFrozenDictionary(
            inputId => inputId,
            inputId => FanOutPlan.For([.. legs.Where(l => l.AcceptsInput(inputId))]));
    }

    // Routes to the per-input queue by SourceEndpointId (per-input backpressure).
    public ValueTask EnqueueAsync(MessageContext context, CancellationToken cancellationToken)
    {
        AgentDiagnostics.QueueDepth.Add(1, new KeyValuePair<string, object?>("queue", $"input:{context.SourceEndpointId}"));
        return _ingressQueues[context.SourceEndpointId].EnqueueAsync(context, cancellationToken);
    }

    // Starts the leg consumers + one consumer per ingress queue. The host calls this once.
    public Task RunAsync(CancellationToken cancellationToken)
    {
        var legTasks = _legs.Select(l => l.RunAsync(cancellationToken));
        foreach (var queue in _ingressQueues.Values)
            _consumers.Add(ConsumeAsync(queue, cancellationToken));
        return Task.WhenAll(_consumers.Concat(legTasks));
    }

    private async Task ConsumeAsync(IMessageChannel ingress, CancellationToken cancellationToken)
    {
        await foreach (var ctx in ingress.ReadAllAsync(cancellationToken))
        {
            AgentDiagnostics.QueueDepth.Add(-1, new KeyValuePair<string, object?>("queue", $"input:{ctx.SourceEndpointId}"));
            var pipeline = await _pipeline.ExecuteAsync(ctx); // parse/validate/filter/enrich, ONCE
            if (pipeline.ShortCircuited)
            {
                // Surface the drop reason (low-cardinality, stage-authored) for observability — parity
                // with the legacy filter's dedicated "message filtered" metric.
                AgentDiagnostics.FilteredMessages.Add(1,
                    new KeyValuePair<string, object?>("source", ctx.SourceEndpointId),
                    new KeyValuePair<string, object?>("reason", pipeline.Reason ?? "unspecified"));
                // Routine, by-design drop (a filter's job is to drop) -> Debug, not Warning, to stay quiet
                // in prod while remaining searchable per message (parity with the legacy filter's log).
                _logger.LogDebug(
                    "Message {CorrelationId} from source {SourceEndpointId} filtered: {Reason}",
                    ctx.CorrelationId, ctx.SourceEndpointId, pipeline.Reason ?? "unspecified");
                ctx.Reply.ReportFiltered(pipeline.Reason);     // whole-message drop -> reply "filtered" (or silent, per contract)
                continue;
            }

            // Fan out via the precomputed per-source plan: applicable legs + required count were
            // resolved once at construction, so there is no per-message LINQ and no leg-count branch
            // here. A single-leg (high-fidelity) plan reuses the envelope in place; a multi-leg plan
            // clones per leg and awaits them together.
            await _fanOutBySource[ctx.SourceEndpointId].DispatchAsync(ctx, cancellationToken);
        }
    }

    public async Task DrainAsync(TimeSpan timeout)
    {
        // 1) stop accepting input and let ingress consumers finish their fan-out
        foreach (var queue in _ingressQueues.Values) queue.Complete();
        try { await Task.WhenAll(_consumers).WaitAsync(timeout); }
        catch (TimeoutException) { }

        // 2) fan-out is done -> now drain each leg
        foreach (var leg in _legs)
            await leg.DrainAsync(timeout);
    }
}
