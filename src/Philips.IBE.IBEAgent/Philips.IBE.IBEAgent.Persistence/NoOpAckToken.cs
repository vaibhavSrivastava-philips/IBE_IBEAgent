using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Persistence;

// §3.9 — a replayed message never produces a second reply (the original reply was already
// settled at first delivery attempt). These sinks are wired onto the reconstructed MessageContext
// so IAckStrategy/ReportLeg calls made during replay have somewhere harmless to go.
public sealed class NoOpAckToken : IAckToken
{
    public static readonly NoOpAckToken Instance = new();

    public Task WriteAsync(ReadOnlyMemory<byte> reply, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class NoOpReplyContext : IReplyContext
{
    public static readonly NoOpReplyContext Instance = new();

    public void Attach(MessageContext message) { }
    public void OnFannedOut(int requiredTotal) { }
    public void ReportFiltered() { }
    public void ReportLeg(bool required, in DeliveryResult result) { }
}
