using Microsoft.Extensions.Logging;
using Philips.IBE.IBEAgent.Abstractions;
namespace Philips.IBE.IBEAgent.Endpoints.Tcp;

internal sealed class TcpConnectionAckToken(Stream stream, SemaphoreSlim writeLock, ILogger logger) : IAckToken
{
    public async Task WriteAsync(ReadOnlyMemory<byte> reply, CancellationToken cancellationToken)
    {
        if (reply.IsEmpty) return;                            // ack disabled => nothing to send

        // Deepest level (Trace) — the full ack body being sent back to the source. Guarded so the
        // decode only runs when Trace is enabled.
        if (logger.IsEnabled(LogLevel.Trace))
            logger.LogTrace("Sending ack ({ByteCount} bytes): {Message}", reply.Length, MessagePreview.ForLog(reply.Span));

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