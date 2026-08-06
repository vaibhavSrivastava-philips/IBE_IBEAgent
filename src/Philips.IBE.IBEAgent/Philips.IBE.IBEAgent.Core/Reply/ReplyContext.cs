using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// Concrete impl of the IReplyContext seam (A2). ONE per received message: one-shot + optional timeout.
// Created by the factory (token+strategy) BEFORE the MessageContext exists; the envelope is Attach()-ed
// right after construction. The strategy writes via message.Ack, so Attach must precede any reply.
public sealed class ReplyContext : IReplyContext, IDisposable
{
    private readonly IAckStrategy _strategy;
    private readonly Timer? _timeout;
    private readonly bool _replyOnFilter;
    private readonly ILogger<ReplyContext> _logger;
    private readonly object _lock = new();
    private readonly List<(int OutputId, DeliveryResult Result)> _legResults = [];
    private MessageContext? _message;
    private int _requiredTotal;
    private int _requiredDone;
    private int _replied;

    public ReplyContext(IAckStrategy strategy, TimeSpan timeout, bool replyOnFilter = true, ILogger<ReplyContext>? logger = null)
    {
        _strategy = strategy;
        _replyOnFilter = replyOnFilter;
        _logger = logger ?? NullLogger<ReplyContext>.Instance;
        if (timeout != Timeout.InfiniteTimeSpan)
            _timeout = new Timer(_ => OnTimeout(), state: null, timeout, Timeout.InfiniteTimeSpan);
    }

    public void Attach(MessageContext message) => _message = message;   // wired by the inbound endpoint

    public void OnFannedOut(int requiredTotal)
    {
        _requiredTotal = requiredTotal;
        if (_strategy.RepliesOnReceipt)                         // Normal/NoAck: reply "received" now
            FireOnce(ReplyOutcome.Received());
        else if (requiredTotal == 0)                            // delivery strategy, but no required leg to await
            FireOnce(ReplyOutcome.Delivered(OrderedResults())); // settle now so the source disposes (else Enhanced/Response only settle at the timeout, or hang if it's infinite)
    }

    public void ReportFiltered(string? reason = null)
    {
        // §6 — a filtered message gets a reply (a reject carrying the filter reason) when ReplyOnFilter is
        // set; otherwise it is a silent drop (consume the one-shot + kill the timeout, write nothing) — the
        // legacy behavior. The reply CODE (e.g. HL7 AR) is the formatter's job, keyed on the Filtered outcome.
        if (_replyOnFilter)
            FireOnce(ReplyOutcome.Filtered(reason));
        else
            Suppress();
    }

    public void ReportLeg(int outputId, bool required, in DeliveryResult result)
    {
        if (!required) return;                                  // optional legs never gate the reply

        lock (_lock)
            _legResults.Add((outputId, result));               // record BEFORE incrementing so the completing leg sees all

        if (result.Outcome == DeliveryOutcome.Delivered)
        {
            if (Interlocked.Increment(ref _requiredDone) >= _requiredTotal)
                FireOnce(ReplyOutcome.Delivered(OrderedResults()));  // all required delivered
        }
        else
        {
            // Any required failure is an overall failure (all-required rule, §6): one NACK now.
            FireOnce(ReplyOutcome.Failed(result.Error, OrderedResults()));
        }
    }

    private void OnTimeout() => FireOnce(ReplyOutcome.Failed("reply timeout", OrderedResults()));

    // Required-leg results ordered by OutputId (deterministic) for the strategy to combine.
    private IReadOnlyList<DeliveryResult> OrderedResults()
    {
        lock (_lock)
            return _legResults.OrderBy(r => r.OutputId).Select(r => r.Result).ToList();
    }

    private void FireOnce(in ReplyOutcome outcome)             // exactly one reply per received message
    {
        if (Interlocked.Exchange(ref _replied, 1) != 0) return; // one-shot
        _timeout?.Dispose();

        var message = _message;
        if (message is null) return;                            // not attached => wiring bug; nothing to write

        // Fire-and-forget: the strategy owns the bytes + token write. The reply path is designed not to
        // throw, but the underlying socket write can fault if the peer disconnected before we replied —
        // observe that fault so it is not lost as an unobserved task exception.
        var replyTask = _strategy.WriteReplyAsync(message, outcome);
        if (!replyTask.IsCompletedSuccessfully)
            ObserveReplyFault(replyTask, message.CorrelationId, message.SourceEndpointId);

        CompleteSource(message, MapCompletion(outcome.Outcome));
    }

    private void ObserveReplyFault(Task replyTask, string correlationId, int sourceEndpointId)
    {
        _ = replyTask.ContinueWith(
            t => _logger.LogDebug(
                t.Exception,
                "Reply for message {CorrelationId} (source {SourceEndpointId}) could not be written (peer likely disconnected before the reply).",
                correlationId, sourceEndpointId),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    // Legacy "silent drop": consume the one-shot and kill the timeout so NO reply is written (not even a
    // later timeout NACK). Used when ReplyOnFilter is disabled.
    private void Suppress()
    {
        if (Interlocked.Exchange(ref _replied, 1) != 0) return;
        _timeout?.Dispose();
        if (_message is { } message)
            CompleteSource(message, MessageCompletion.Filtered);   // still consume the source (e.g. move/mark the file)
    }

    // Signal the SOURCE side that the message has settled so it can dispose the source artifact (a File
    // move/watermark). Distinct from the reply (bytes); no-op when there is nothing to dispose. Fire-and-
    // observe: a faulted disposition is logged, never thrown (reply-path parity).
    private void CompleteSource(MessageContext message, MessageCompletion outcome)
    {
        var disposition = message.Disposition;
        if (disposition is null) return;

        var task = disposition.CompleteAsync(outcome, CancellationToken.None);
        if (!task.IsCompletedSuccessfully)
            ObserveDispositionFault(task.AsTask(), message.CorrelationId, message.SourceEndpointId);
    }

    private void ObserveDispositionFault(Task task, string correlationId, int sourceEndpointId)
    {
        _ = task.ContinueWith(
            t => _logger.LogDebug(
                t.Exception,
                "Source disposition for message {CorrelationId} (source {SourceEndpointId}) could not complete.",
                correlationId, sourceEndpointId),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static MessageCompletion MapCompletion(DeliveryOutcome outcome) => outcome switch
    {
        DeliveryOutcome.Delivered => MessageCompletion.Completed,
        DeliveryOutcome.Filtered => MessageCompletion.Filtered,
        DeliveryOutcome.Accepted => MessageCompletion.Completed,   // Normal-ack "received"
        _ => MessageCompletion.Faulted,                            // Failed
    };

    public void Dispose() => _timeout?.Dispose();
}