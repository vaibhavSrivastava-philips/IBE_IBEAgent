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
    private readonly List<Task> _consumers = [];

    // Exposed so the host composition root can build the ForwardWorker's IReplayTargetRegistry
    // (§3.9) without the Persistence layer needing to know about ContractRuntime internals.
    public IReadOnlyList<DeliveryLeg> Legs => _legs;

    public ContractRuntime(
        IReadOnlyDictionary<int, IMessageChannel> ingressQueues,
        IMessagePipeline pipeline,
        IReadOnlyList<DeliveryLeg> legs)
    {
        _ingressQueues = ingressQueues;
        _pipeline = pipeline;
        _legs = legs;
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
                AgentDiagnostics.FilteredMessages.Add(1, new KeyValuePair<string, object?>("source", ctx.SourceEndpointId));
                ctx.Reply.ReportFiltered();                   // whole-message drop -> reply "filtered"
                continue;
            }

            // Per-leg input filter: only fan out to legs that accept this message's source.
            var applicable = _legs.Where(l => l.AcceptsInput(ctx.SourceEndpointId)).ToList();
            var requiredCount = applicable.Count(l => l.Required);

            ctx.Reply.OnFannedOut(requiredCount);             // arm per-message; Normal ack fires "received" here
            await Task.WhenAll(applicable.Select(l =>
                l.EnqueueAsync(ctx.CloneForLeg(l.OutputId), cancellationToken).AsTask()));
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
