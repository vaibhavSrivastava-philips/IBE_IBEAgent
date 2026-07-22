namespace Philips.IBE.IBEAgent.Abstractions;

// protocol-bound; writes reply BYTES back over this source transport. Carries content, not just status.
public interface IAckToken
{
    Task WriteAsync(ReadOnlyMemory<byte> reply, CancellationToken cancellationToken);
}