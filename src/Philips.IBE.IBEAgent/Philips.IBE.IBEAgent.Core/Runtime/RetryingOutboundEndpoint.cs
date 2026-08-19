using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;

namespace Philips.IBE.IBEAgent.Core;

// §3.7 — inline per-leg retry decorator around a transport IOutboundEndpoint. Retries a Failed
// outcome or a thrown transport exception up to RetryOptions.MaxAttempts with Fixed/Exponential
// backoff, then yields the last result (an exhausted AtLeastOnce leg then falls through to
// store-and-forward). Filtered/Delivered/Accepted and cancellation are never retried.
public sealed class RetryingOutboundEndpoint : IOutboundEndpoint
{
    private readonly IOutboundEndpoint _inner;
    private readonly RetryOptions _retry;
    private readonly ILogger<RetryingOutboundEndpoint> _logger;

    public RetryingOutboundEndpoint(IOutboundEndpoint inner, RetryOptions retry, ILogger<RetryingOutboundEndpoint>? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _retry = retry ?? throw new ArgumentNullException(nameof(retry));
        _logger = logger ?? NullLogger<RetryingOutboundEndpoint>.Instance;
    }

    public async Task<DeliveryResult> SendAsync(MessageContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Durable replays get exactly one attempt: the ForwardWorker is the sole retry/backoff
        // authority for stored messages, so inline retry must not compound with it on replay.
        var maxAttempts = context.IsReplay ? 1 : Math.Max(1, _retry.MaxAttempts);

        for (var attempt = 1; ; attempt++)
        {
            DeliveryResult result;
            try
            {
                result = await _inner.SendAsync(context, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < maxAttempts)
            {
                // Transient throw (e.g. a stale pooled connection). On the final attempt this guard
                // is false, so the exception propagates unchanged for the leg to log and store.
                var delay = Backoff(attempt);
                _logger.LogDebug(
                    "Delivery attempt {Attempt}/{MaxAttempts} threw {Failure}; retrying in {DelaySeconds}s.",
                    attempt, maxAttempts, ex.GetType().Name, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            if (result.Outcome != DeliveryOutcome.Failed || attempt >= maxAttempts)
                return result;

            var backoff = Backoff(attempt);
            _logger.LogDebug(
                "Delivery attempt {Attempt}/{MaxAttempts} failed ({Reason}); retrying in {DelaySeconds}s.",
                attempt, maxAttempts, result.Error, backoff.TotalSeconds);
            await Task.Delay(backoff, cancellationToken);
        }
    }

    private TimeSpan Backoff(int attempt)
    {
        var seconds = _retry.Backoff == BackoffKind.Exponential
            ? _retry.BackoffSeconds * Math.Pow(2, attempt - 1)
            : _retry.BackoffSeconds;
        return TimeSpan.FromSeconds(seconds);
    }
}
