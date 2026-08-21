using Microsoft.Extensions.Logging;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Endpoints.Tcp;

internal sealed class TcpDuplexSession(int sourceEndpointId, Stream stream, SemaphoreSlim writeLock) : IDisposable
{
    private TaskCompletionSource<ReadOnlyMemory<byte>>? _pendingReply;
    // Serializes the entire send+reply cycle so a second concurrent sender cannot stomp
    // _pendingReply before the first caller receives its reply.  WriteLock is released
    // before the reply wait so inbound ACK writes on the same stream never deadlock.
    private readonly SemaphoreSlim _sendGate = new(1, 1);

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
        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            TaskCompletionSource<ReadOnlyMemory<byte>>? pending = null;
            if (expectReply)
                pending = new TaskCompletionSource<ReadOnlyMemory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);

            Interlocked.Exchange(ref _pendingReply, pending);

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
                Interlocked.CompareExchange(ref _pendingReply, null, pending);
                return new DeliveryResult(DeliveryOutcome.Failed, "no MLLP ack received");
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                Interlocked.CompareExchange(ref _pendingReply, null, pending);
                return new DeliveryResult(DeliveryOutcome.Failed, ex.Message);
            }
        }
        finally { _sendGate.Release(); }
    }

    public void Dispose()
    {
        var pending = Interlocked.Exchange(ref _pendingReply, null);
        pending?.TrySetCanceled();
        _sendGate.Dispose();
    }
}
