// WebSocketOutboundEndpoint.cs
using System.Net.WebSockets;
using Philips.IBE.IBEAgent.Abstractions;
namespace Philips.IBE.IBEAgent.Endpoints.WebSocket;

public sealed class WebSocketOutboundEndpoint : IOutboundEndpoint, IAsyncDisposable
{
    private readonly WebSocketOutboundOptions _options;
    private readonly IMessageCodec? _codec;
    private readonly WebSocketConnectionPool _pool;

    public WebSocketOutboundEndpoint(WebSocketOutboundOptions options, IMessageCodec? codec)
    {
        _options = options; _codec = codec;
        _pool = new WebSocketConnectionPool(options.Endpoint, options.PoolSize, options.Ssl, options.Proxy);
    }

    public async Task<DeliveryResult> SendAsync(MessageContext context, CancellationToken cancellationToken)
    {
        ClientWebSocket? socket = null;
        bool healthy = false;
        try
        {
            var wire = _codec?.Encode(context) ?? context.Payload;   // canonical model -> destination bytes

            socket = await _pool.RentAsync(cancellationToken);
            await socket.SendAsync(wire, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);

            if (_options.ExpectReply)
            {
                using var acc = new MemoryStream();
                var buffer = new byte[_options.ReceiveBufferSize];
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return new DeliveryResult(DeliveryOutcome.Failed, "peer closed before reply"); // stream closed
                    acc.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                healthy = true;
                return new DeliveryResult(DeliveryOutcome.Delivered,
                    ResponsePayload: acc.ToArray(), ResponseFormat: context.Format);
            }

            healthy = true;
            return new DeliveryResult(DeliveryOutcome.Delivered);
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or OperationCanceledException)
        {
            return new DeliveryResult(DeliveryOutcome.Failed, ex.Message);
        }
        finally
        {
            if (socket is not null) { if (healthy) _pool.Return(socket); else _pool.Discard(socket); }
        }
    }

    public ValueTask DisposeAsync() => _pool.DisposeAsync();
}
