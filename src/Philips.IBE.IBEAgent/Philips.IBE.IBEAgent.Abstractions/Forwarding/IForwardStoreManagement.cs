namespace Philips.IBE.IBEAgent.Abstractions;

public interface IForwardStoreManagement
{
    Task<IReadOnlyList<ForwardEntry>> ListAsync(ForwardStatus? status, int max, CancellationToken cancellationToken);
    Task<bool> RequeueAsync(long id, CancellationToken cancellationToken);
    Task<bool> DiscardAsync(long id, string? reason, CancellationToken cancellationToken);
}
