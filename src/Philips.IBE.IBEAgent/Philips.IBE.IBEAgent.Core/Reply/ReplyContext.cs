using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// Concrete impl of the IReplyContext seam (A2). ONE per received message: one-shot + optional timeout.
// Created by the factory (token+strategy) BEFORE the MessageContext exists; the envelope is Attach()-ed
// right after construction. The strategy writes via message.Ack, so Attach must precede any reply.
public sealed class ReplyContext : IReplyContext, IDisposable
{
    private readonly IAckStrategy _strategy;
    private readonly Timer? _timeout;
    private MessageContext? _message;
    private int _requiredTotal;
    private int _requiredDone;
    private int _replied;

    public ReplyContext(IAckStrategy strategy, TimeSpan timeout)
    {
        _strategy = strategy;
        if (timeout != Timeout.InfiniteTimeSpan)
            _timeout = new Timer(_ => OnTimeout(), state: null, timeout, Timeout.InfiniteTimeSpan);
    }

    public void Attach(MessageContext message) => _message = message;   // wired by the inbound endpoint

    public void OnFannedOut(int requiredTotal)
    {
        _requiredTotal = requiredTotal;
        if (_strategy.RepliesOnReceipt)                         // Normal ack: reply "received" now
            FireOnce(new DeliveryResult(DeliveryOutcome.Accepted));
    }

    public void ReportFiltered() => FireOnce(new DeliveryResult(DeliveryOutcome.Filtered));

    public void ReportLeg(bool required, in DeliveryResult result)
    {
        if (!required) return;                                  // optional legs never gate the reply
        if (result.Outcome == DeliveryOutcome.Delivered)
        {
            if (Interlocked.Increment(ref _requiredDone) >= _requiredTotal)
                FireOnce(result);                               // all required delivered -> positive reply
        }
        else
        {
            FireOnce(new DeliveryResult(DeliveryOutcome.Failed, result.Error));  // one required failure -> NACK
        }
    }

    private void OnTimeout() => FireOnce(new DeliveryResult(DeliveryOutcome.Failed, "reply timeout"));

    private void FireOnce(in DeliveryResult result)             // exactly one reply per received message
    {
        if (Interlocked.Exchange(ref _replied, 1) != 0) return; // one-shot
        _timeout?.Dispose();

        var message = _message;
        if (message is null) return;                            // not attached => wiring bug; nothing to write
        _ = _strategy.WriteReplyAsync(message, result);         // fire-and-forget; strategy owns bytes + token write
    }

    public void Dispose() => _timeout?.Dispose();
}