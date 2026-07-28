namespace Philips.IBE.IBEAgent.Configuration;

// §8 — one entry per input comm point; per-input isolation (Capacity/DOP/Ordered/OverflowPolicy).
public sealed record InputOptions
{
    public required int InputId { get; init; }
    public ChannelOptions Channel { get; init; } = new();
}
