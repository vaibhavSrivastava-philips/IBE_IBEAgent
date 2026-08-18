using System.Net.WebSockets;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Endpoints.WebSocket;

internal sealed class WebSocketDuplexSession(int sourceEndpointId, System.Net.WebSockets.WebSocket socket, SemaphoreSlim writeLock, bool ownsWriteLock = true) : IDisposable
{
    private TaskCompletionSource<ReadOnlyMemory<byte>>? _pendingReply;

    public int SourceEndpointId { get; } = sourceEndpointId;
    public System.Net.WebSockets.WebSocket Socket { get; } = socket;
    public SemaphoreSlim WriteLock { get; } = writeLock;

    public WebSocketAckToken CreateAckToken() => new(Socket, WriteLock);

    public bool TryCompletePendingReply(ReadOnlyMemory<byte> payload)
    {
        var pending = Interlocked.Exchange(ref _pendingReply, null);
        if (pending is null) return false;
        pending.TrySetResult(payload);
        return true;
    }

    public async Task<DeliveryResult> SendAsync(
        ReadOnlyMemory<byte> payload,
        bool expectReply,
        TimeSpan replyTimeout,
        string responseFormat,
        CancellationToken cancellationToken)
    {
        if (Socket.State != WebSocketState.Open)
            return new DeliveryResult(DeliveryOutcome.Failed, "WebSocket session is not open.");

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
                if (Socket.State != WebSocketState.Open)
                    return new DeliveryResult(DeliveryOutcome.Failed, "WebSocket session is not open.");
                await Socket.SendAsync(payload, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);
            }
            finally { WriteLock.Release(); }

            if (pending is null)
                return new DeliveryResult(DeliveryOutcome.Delivered);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(replyTimeout);
            var reply = await pending.Task.WaitAsync(timeoutCts.Token);
            return new DeliveryResult(DeliveryOutcome.Delivered, ResponsePayload: reply, ResponseFormat: responseFormat);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (pending is not null)
                Interlocked.CompareExchange(ref _pendingReply, null, pending);
            return new DeliveryResult(DeliveryOutcome.Failed, "no WebSocket reply received");
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or OperationCanceledException)
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
        if (ownsWriteLock)
            WriteLock.Dispose();
    }
}
