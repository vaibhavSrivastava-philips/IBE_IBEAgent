using Philips.IBE.IBEAgent.Abstractions;
namespace Philips.IBE.IBEAgent.Endpoints.Tcp;

internal sealed class TcpConnectionAckToken(Stream stream, SemaphoreSlim writeLock) : IAckToken
{
    public async Task WriteAsync(ReadOnlyMemory<byte> reply, CancellationToken cancellationToken)
    {
        if (reply.IsEmpty) return;                            // ack disabled => nothing to send
        var framed = MllpFramer.Frame(reply.Span);
        await writeLock.WaitAsync(cancellationToken);         // serialize concurrent writes on one socket
        try
        {
            await stream.WriteAsync(framed, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally { writeLock.Release(); }
    }
}