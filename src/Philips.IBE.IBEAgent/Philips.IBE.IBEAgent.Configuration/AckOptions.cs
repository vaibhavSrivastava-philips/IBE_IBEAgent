using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Configuration;

// §6/§8 — the two configured ack modes over required legs (Normal | Enhanced), plus shape.
public sealed record AckOptions
{
    public bool IsEnabled { get; init; } = true;
    public bool IsEnhanced { get; init; }                      // false = Normal ("received"); true = Enhanced (reflects delivery)
    public AckShape Shape { get; init; } = AckShape.Single;
    public int TimeoutMs { get; init; } = 30_000;              // Enhanced only: max wait for delivery before NACK; <=0 = no timeout (Normal fires on receipt, so this is inert)
}
