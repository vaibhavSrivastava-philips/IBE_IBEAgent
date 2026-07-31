using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Configuration;

// §8 — per-leg inline retry (Polly-style decorator config); exhausted retries fall through to
// store-and-forward (Phase 6), not modeled here.
public sealed record RetryOptions
{
    public int MaxAttempts { get; init; } = 3;
    public int BackoffSeconds { get; init; } = 2;
    public BackoffKind Backoff { get; init; } = BackoffKind.Exponential;
}
