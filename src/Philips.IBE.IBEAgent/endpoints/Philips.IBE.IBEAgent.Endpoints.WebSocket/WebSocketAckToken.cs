using System.Net.WebSockets;
using Philips.IBE.IBEAgent.Abstractions;
namespace Philips.IBE.IBEAgent.Endpoints.WebSocket;

internal sealed class WebSocketAckToken(System.Net.WebSockets.WebSocket socket, SemaphoreSlim writeLock) : IAckToken
{
    public async Task WriteAsync(ReadOnlyMemory<byte> reply, CancellationToken cancellationToken)
    {
        if (reply.IsEmpty) return;                            // ack disabled => nothing to send
        await writeLock.WaitAsync(cancellationToken);         // serialize concurrent writes on one socket
        try
        {
            if (socket.State != WebSocketState.Open) return;
            await socket.SendAsync(reply, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);
        }
        finally { writeLock.Release(); }
    }
}
