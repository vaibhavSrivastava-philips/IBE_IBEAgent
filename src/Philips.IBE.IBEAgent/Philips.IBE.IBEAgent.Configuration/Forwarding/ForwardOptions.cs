using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Configuration;

// §3.9/§8 — ForwardWorker tuning: poll cadence, attempt cap, and backoff between retries of
// Pending store-and-forward rows. Read from "Forward".
public sealed record ForwardOptions
{
    public ForwardOwner Owner { get; init; } = ForwardOwner.InProcess;
    public int PollIntervalSeconds { get; init; } = 5;
    public int MaxAttempts { get; init; } = 5;
    public int InitialBackoffSeconds { get; init; } = 5;
    public BackoffKind Backoff { get; init; } = BackoffKind.Exponential;
    public int FetchBatchSize { get; init; } = 50;
}
