using System.Diagnostics;
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
    private readonly string _contractName;      // for per-message flow monitoring (contract identity)
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
        ILogger<DeliveryLeg>? logger = null,
        string? contractName = null)
    {
        OutputId = outputId;
        Required = required;
        _queue = queue;
        _endpoint = endpoint;
        FromInputIds = fromInputIds;
        RouteWhen = routeWhen;
        _forward = forward;
        _logger = logger ?? NullLogger<DeliveryLeg>.Instance;
        _contractName = contractName ?? "unknown";
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

            // Deepest level (Trace) — the full outbound message body being delivered. Guarded so the
            // decode only runs when Trace is enabled.
            if (_logger.IsEnabled(LogLevel.Trace))
                _logger.LogTrace(
                    "Outbound message {CorrelationId} to output {OutputId} body: {Message}",
                    ctx.CorrelationId, OutputId, MessagePreview.ForLog(ctx.Payload.Span));

            var startedAt = Stopwatch.GetTimestamp();
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

            var sendMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
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
                        "Delivery of message {CorrelationId} for contract {ContractName} from source {SourceEndpointId} to output {OutputId} errored unexpectedly; {Disposition}.",
                        ctx.CorrelationId, _contractName, ctx.SourceEndpointId, OutputId, disposition);
                else
                    _logger.LogWarning(
                        "Delivery of message {CorrelationId} for contract {ContractName} from source {SourceEndpointId} to output {OutputId} failed ({Reason}); {Disposition}.",
                        ctx.CorrelationId, _contractName, ctx.SourceEndpointId, OutputId, result.Error, disposition);

                if (_forward is not null)
                    await _forward.StoreAsync(ctx, OutputId, result.Error, cancellationToken); // Pending (Phase 6)
            }
            else
            {
                // Monitoring (Information) — one line per forwarded message (the primary production
                // signal that a message reached its destination). ElapsedMs is END-TO-END (reception ->
                // delivery, incl. queue + pipeline); SendMs isolates the outbound hop.
                var e2eMs = (long)Stopwatch.GetElapsedTime(ctx.ReceivedTimestamp).TotalMilliseconds;
                _logger.LogInformation(
                    "Delivered message {CorrelationId} for contract {ContractName} from source {SourceEndpointId} to output {OutputId} in {ElapsedMs}ms (send {SendMs}ms).",
                    ctx.CorrelationId, _contractName, ctx.SourceEndpointId, OutputId, e2eMs, sendMs);
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
