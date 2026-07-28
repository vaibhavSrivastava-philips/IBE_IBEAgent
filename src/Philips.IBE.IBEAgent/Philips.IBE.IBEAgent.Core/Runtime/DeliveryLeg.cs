using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Telemetry;

namespace Philips.IBE.IBEAgent.Core;

// One output. Owns its queue + outbound endpoint; delivers; reports to the message's ReplyContext.
// NO per-leg processing pipeline — a leg only encodes (via the endpoint's codec) + delivers.
public sealed class DeliveryLeg : IReplayTarget
{
    private readonly IMessageChannel _queue;
    private readonly IOutboundEndpoint _endpoint;
    private readonly IForwardStore? _forward;   // null in the slice-1 in-memory path; wired in Phase 6
    private Task? _runTask;

    public int OutputId { get; }
    public bool Required { get; }
    public IReadOnlySet<int>? FromInputIds { get; }

    public DeliveryLeg(
        int outputId,
        bool required,
        IMessageChannel queue,
        IOutboundEndpoint endpoint,
        IReadOnlySet<int>? fromInputIds = null,
        IForwardStore? forward = null)
    {
        OutputId = outputId;
        Required = required;
        _queue = queue;
        _endpoint = endpoint;
        FromInputIds = fromInputIds;
        _forward = forward;
    }

    // Per-leg input filter: null/empty = accept all inputs (default, backward compatible).
    public bool AcceptsInput(int sourceEndpointId)
        => FromInputIds is null || FromInputIds.Count == 0 || FromInputIds.Contains(sourceEndpointId);

    public ValueTask EnqueueAsync(MessageContext context, CancellationToken cancellationToken)
    {
        AgentDiagnostics.QueueDepth.Add(1, new KeyValuePair<string, object?>("queue", $"leg:{OutputId}"));
        return _queue.EnqueueAsync(context, cancellationToken);
    }

    // Leg-targeted replay (Phase 6): reuses THIS leg's path; never re-routes/re-processes/re-replies.
    public ValueTask ReplayAsync(MessageContext context, CancellationToken cancellationToken)
    {
        context.MarkReplay();
        return _queue.EnqueueAsync(context, cancellationToken);
    }

    public Task RunAsync(CancellationToken cancellationToken)
        => _runTask ??= ConsumeAsync(cancellationToken);

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        await foreach (var ctx in _queue.ReadAllAsync(cancellationToken))
        {
            AgentDiagnostics.QueueDepth.Add(-1, new KeyValuePair<string, object?>("queue", $"leg:{OutputId}"));
            using var activity = AgentDiagnostics.StartLegDelivery(OutputId);
            DeliveryResult result;
            try
            {
                result = await _endpoint.SendAsync(ctx, cancellationToken); // serialize (codec) + send; retries inside
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = new DeliveryResult(DeliveryOutcome.Failed, ex.Message);
            }

            AgentDiagnostics.Deliveries.Add(1,
                new KeyValuePair<string, object?>("outputId", OutputId),
                new KeyValuePair<string, object?>("outcome", result.Outcome.ToString()));

            if (result.Outcome != DeliveryOutcome.Delivered)
            {
                if (_forward is not null)
                    await _forward.StoreAsync(ctx, OutputId, result.Error, cancellationToken); // Pending (Phase 6)
            }
            else if (ctx.IsReplay && _forward is not null)
            {
                await _forward.ResolveAsync(ctx, OutputId, cancellationToken); // replay delivered -> clear entry
            }

            if (!ctx.IsReplay)
                ctx.Reply.ReportLeg(Required, result); // FRESH only: a replay never produces a second reply
        }
    }

    public async Task DrainAsync(TimeSpan timeout)
    {
        _queue.Complete();
        if (_runTask is not null)
        {
            try { await _runTask.WaitAsync(timeout); }
            catch (TimeoutException) { }
        }
    }
}
