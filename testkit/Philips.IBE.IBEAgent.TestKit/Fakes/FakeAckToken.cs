using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.TestKit;

public sealed class FakeAckToken : IAckToken
{
    private readonly List<byte[]> _writes = [];
    public IReadOnlyList<byte[]> Writes => _writes;
    public int WriteCount => _writes.Count;

    public Task WriteAsync(ReadOnlyMemory<byte> reply, CancellationToken cancellationToken)
    {
        _writes.Add(reply.ToArray());
        return Task.CompletedTask;
    }
}