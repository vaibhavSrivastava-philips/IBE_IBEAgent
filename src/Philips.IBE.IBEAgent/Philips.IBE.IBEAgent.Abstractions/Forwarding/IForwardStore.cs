namespace Philips.IBE.IBEAgent.Abstractions;

public interface IForwardStore              // §3.9 — one durable buffer, tagged by OutputId.
{
    Task StoreAsync(MessageContext context, int outputId, string? error, CancellationToken cancellationToken);
    Task ResolveAsync(MessageContext context, int outputId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ForwardEntry>> FetchDueAsync(int max, CancellationToken cancellationToken);
    Task RescheduleAsync(long id, int attempts, DateTimeOffset nextAttemptAt, string? lastError, CancellationToken cancellationToken);
    Task ParkAsync(long id, string reason, CancellationToken cancellationToken);
}