using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Configuration;

// §8/§13 — per-queue settings, shared shape for both Inputs and Outputs (symmetric).
public sealed record ChannelOptions
{
    public int Capacity { get; init; } = 1024;
    public int DegreeOfParallelism { get; init; } = 1;
    public bool Ordered { get; init; }
    public OverflowPolicy OverflowPolicy { get; init; } = OverflowPolicy.Wait;
}
