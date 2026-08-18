using Microsoft.Extensions.Logging;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Endpoints.Tcp;

internal sealed class TcpDuplexSession(int sourceEndpointId, Stream stream, SemaphoreSlim writeLock) : IDisposable
{
    private TaskCompletionSource<ReadOnlyMemory<byte>>? _pendingReply;

    public int SourceEndpointId { get; } = sourceEndpointId;
    public Stream Stream { get; } = stream;
    public SemaphoreSlim WriteLock { get; } = writeLock;

    public TcpConnectionAckToken CreateAckToken(ILogger logger) => new(Stream, WriteLock, logger);

    public bool TryCompletePendingReply(byte[] frame)
    {
        var pending = Interlocked.Exchange(ref _pendingReply, null);
        if (pending is null) return false;
        pending.TrySetResult(frame);
        return true;
    }

    public async Task<DeliveryResult> SendAsync(
        ReadOnlyMemory<byte> framed,
        bool expectReply,
        TimeSpan replyTimeout,
        string responseFormat,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<ReadOnlyMemory<byte>>? pending = null;
        if (expectReply)
        {
            pending = new TaskCompletionSource<ReadOnlyMemory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var previous = Interlocked.Exchange(ref _pendingReply, pending);
            previous?.TrySetCanceled(cancellationToken);
        }

        try
        {
            await WriteLock.WaitAsync(cancellationToken);
            try
            {
                await Stream.WriteAsync(framed, cancellationToken);
                await Stream.FlushAsync(cancellationToken);
            }
            finally { WriteLock.Release(); }

            if (pending is null)
                return new DeliveryResult(DeliveryOutcome.Delivered);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(replyTimeout);
            var replyBytes = await pending.Task.WaitAsync(timeoutCts.Token);
            return new DeliveryResult(DeliveryOutcome.Delivered, ResponsePayload: replyBytes, ResponseFormat: responseFormat);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (pending is not null)
                Interlocked.CompareExchange(ref _pendingReply, null, pending);
            return new DeliveryResult(DeliveryOutcome.Failed, "no MLLP ack received");
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            if (pending is not null)
                Interlocked.CompareExchange(ref _pendingReply, null, pending);
            return new DeliveryResult(DeliveryOutcome.Failed, ex.Message);
        }
    }

    public void Dispose()
    {
        var pending = Interlocked.Exchange(ref _pendingReply, null);
        pending?.TrySetCanceled();
    }
}
