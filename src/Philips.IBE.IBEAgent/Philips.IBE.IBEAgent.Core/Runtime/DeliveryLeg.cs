using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger<DeliveryLeg> _logger;
    private Task? _runTask;

    public int OutputId { get; }
    public bool Required { get; }
    public IReadOnlySet<int>? FromInputIds { get; }
    public IReadOnlyDictionary<string, string>? RouteWhen { get; }

    public DeliveryLeg(
        int outputId,
        bool required,
        IMessageChannel queue,
        IOutboundEndpoint endpoint,
        IReadOnlySet<int>? fromInputIds = null,
        IReadOnlyDictionary<string, string>? routeWhen = null,
        IForwardStore? forward = null,
        ILogger<DeliveryLeg>? logger = null)
    {
        OutputId = outputId;
        Required = required;
        _queue = queue;
        _endpoint = endpoint;
        FromInputIds = fromInputIds;
        RouteWhen = routeWhen;
        _forward = forward;
        _logger = logger ?? NullLogger<DeliveryLeg>.Instance;
    }

    // Per-leg input filter: null/empty = accept all inputs (default, backward compatible).
    public bool AcceptsInput(int sourceEndpointId)
        => FromInputIds is null || FromInputIds.Count == 0 || FromInputIds.Contains(sourceEndpointId);

    // True when this leg carries a content filter (RouteWhen); such legs are resolved per message.
    public bool HasRouteWhen => RouteWhen is { Count: > 0 };

    // Per-leg content filter: every RouteWhen pair must equal a message header (AND, exact ordinal).
    // null/empty = accept all messages. Facts are written by a classifier stage in the shared pipeline;
    // this is a dumb string compare so Core stays content-agnostic.
    public bool AcceptsMessage(IDictionary<string, string> headers)
    {
        if (RouteWhen is null || RouteWhen.Count == 0)
            return true;

        foreach (var (key, expected) in RouteWhen)
        {
            if (!headers.TryGetValue(key, out var actual) || !string.Equals(actual, expected, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

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
            Exception? failure = null;
            try
            {
                result = await _endpoint.SendAsync(ctx, cancellationToken); // serialize (codec) + send; retries inside
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failure = ex;
                result = new DeliveryResult(DeliveryOutcome.Failed, ex.Message);
            }

            AgentDiagnostics.Deliveries.Add(1,
                new KeyValuePair<string, object?>("outputId", OutputId),
                new KeyValuePair<string, object?>("outcome", result.Outcome.ToString()));

            if (result.Outcome != DeliveryOutcome.Delivered)
            {
                // Store-vs-drop is decided by the leg's delivery guarantee (a forward store is only
                // wired for AtLeastOnce). An exception escaping the endpoint is unexpected (Error);
                // a categorized transport failure the endpoint already handled is a Warning.
                var disposition = _forward is not null ? "stored for retry" : "dropped (AtMostOnce, no retry)";
                if (failure is not null)
                    _logger.LogError(failure,
                        "Unexpected error delivering message {CorrelationId} to output {OutputId}; {Disposition}.",
                        ctx.CorrelationId, OutputId, disposition);
                else
                    _logger.LogWarning(
                        "Delivery of message {CorrelationId} to output {OutputId} failed ({Reason}); {Disposition}.",
                        ctx.CorrelationId, OutputId, result.Error, disposition);

                if (_forward is not null)
                    await _forward.StoreAsync(ctx, OutputId, result.Error, cancellationToken); // Pending (Phase 6)
            }
            else
            {
                _logger.LogDebug("Delivered message {CorrelationId} to output {OutputId}.", ctx.CorrelationId, OutputId);
                if (ctx.IsReplay && _forward is not null)
                    await _forward.ResolveAsync(ctx, OutputId, cancellationToken); // replay delivered -> clear entry
            }

            if (!ctx.IsReplay)
                ctx.Reply.ReportLeg(OutputId, Required, result); // FRESH only: a replay never produces a second reply
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
