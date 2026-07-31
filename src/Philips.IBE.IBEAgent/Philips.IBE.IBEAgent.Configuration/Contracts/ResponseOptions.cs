namespace Philips.IBE.IBEAgent.Configuration;

// §6.1/§8 — request-reply: return the responder leg's captured payload instead of an ack.
// Mutually exclusive with an enabled Acknowledgement (validated).
public sealed record ResponseOptions
{
    public bool IsEnabled { get; init; }
    public int? FromOutputId { get; init; }        // the single responder leg (defaults to the sole required output)
    public int TimeoutMs { get; init; } = 30_000;  // mandatory wait; on timeout -> protocol error reply, release source
}
