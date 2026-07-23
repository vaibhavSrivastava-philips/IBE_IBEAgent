// TcpOutboundEndpoint.cs
using System.Net.Sockets;
using Philips.IBE.IBEAgent.Abstractions;
namespace Philips.IBE.IBEAgent.Endpoints.Tcp;

public sealed class TcpOutboundEndpoint : IOutboundEndpoint, IAsyncDisposable
{
    private readonly TcpOutboundOptions _options;
    private readonly IMessageCodec? _codec;
    private readonly TcpConnectionPool _pool;

    public TcpOutboundEndpoint(TcpOutboundOptions options, IMessageCodec? codec)
    {
        _options = options; _codec = codec;
        _pool = new TcpConnectionPool(options.Host, options.Port, options.PoolSize);
    }

    public async Task<DeliveryResult> SendAsync(MessageContext context, CancellationToken cancellationToken)
    {
        TcpClient? client = null;
        bool healthy = false;
        try
        {
            var wire = _codec?.Encode(context) ?? context.Payload;                    // canonical model -> destination bytes
            var framed = MllpFramer.Frame(wire.Span);

            client = await _pool.RentAsync(cancellationToken);
            var stream = client.GetStream();
            await stream.WriteAsync(framed, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            if (_options.ExpectReply)
            {
                await foreach (var replyBytes in MllpFramer.ReadMessagesAsync(stream, cancellationToken))
                {
                    healthy = true;
                    return new DeliveryResult(DeliveryOutcome.Delivered,
                        ResponsePayload: replyBytes, ResponseFormat: context.Format);
                }
                return new DeliveryResult(DeliveryOutcome.Failed, "no MLLP ack received"); // stream closed
            }

            healthy = true;
            return new DeliveryResult(DeliveryOutcome.Delivered);
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            return new DeliveryResult(DeliveryOutcome.Failed, ex.Message);
        }
        finally
        {
            if (client is not null) { if (healthy) _pool.Return(client); else _pool.Discard(client); }
        }
    }

    public ValueTask DisposeAsync() => _pool.DisposeAsync();
}