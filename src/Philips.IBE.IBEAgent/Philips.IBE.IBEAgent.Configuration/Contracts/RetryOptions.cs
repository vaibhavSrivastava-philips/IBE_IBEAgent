using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Configuration;

// §3.7/§8 — per-leg inline retry, consumed by the RetryingOutboundEndpoint decorator. Default
// MaxAttempts=1 means retry is DISABLED unless a contract opts in; exhausted retries then fall
// through to store-and-forward for AtLeastOnce legs.
public sealed record RetryOptions
{
    public int MaxAttempts { get; init; } = 1;   // total attempts (1 = no retry); > 1 enables the decorator
    public int BackoffSeconds { get; init; } = 2;
    public BackoffKind Backoff { get; init; } = BackoffKind.Exponential;
}
