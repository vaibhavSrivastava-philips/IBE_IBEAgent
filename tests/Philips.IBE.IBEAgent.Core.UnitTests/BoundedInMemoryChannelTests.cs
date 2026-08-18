using System.Text;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class BoundedInMemoryChannelTests
{
    [Fact]
    public void Constructor_rejects_nonpositive_capacity()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedInMemoryChannel(0));

    [Fact]
    public void Constructor_rejects_spill_to_disk()
        => Assert.Throws<NotSupportedException>(() => new BoundedInMemoryChannel(4, OverflowPolicy.SpillToDisk));

    [Fact]
    public async Task Enqueue_then_read_returns_items_in_order()
    {
        var channel = new BoundedInMemoryChannel(8);
        foreach (var s in new[] { "a", "b", "c" })
            await channel.EnqueueAsync(MessageContextBuilder.Create(payload: s), CancellationToken.None);
        channel.Complete();

        var payloads = new List<string>();
        await foreach (var ctx in channel.ReadAllAsync(CancellationToken.None))
            payloads.Add(Payload(ctx));

        Assert.Equal(new[] { "a", "b", "c" }, payloads);
    }

    [Fact]
    public async Task Reject_overflow_throws_when_full()
    {
        var channel = new BoundedInMemoryChannel(1, OverflowPolicy.Reject);
        await channel.EnqueueAsync(MessageContextBuilder.Create(payload: "1"), CancellationToken.None);

        await Assert.ThrowsAsync<QueueFullException>(async () =>
            await channel.EnqueueAsync(MessageContextBuilder.Create(payload: "2"), CancellationToken.None));
    }

    [Fact]
    public async Task Wait_overflow_backpressures_until_space_frees()
    {
        var channel = new BoundedInMemoryChannel(1, OverflowPolicy.Wait);
        await channel.EnqueueAsync(MessageContextBuilder.Create(payload: "1"), CancellationToken.None);

        var second = channel.EnqueueAsync(MessageContextBuilder.Create(payload: "2"), CancellationToken.None).AsTask();
        Assert.False(second.IsCompleted);   // queue full -> writer blocked

        await using var reader = channel.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal("1", Payload(reader.Current));

        await second.WaitAsync(TimeSpan.FromSeconds(2));   // slot freed -> completes
        Assert.True(second.IsCompleted);
    }

    private static string Payload(MessageContext ctx) => Encoding.UTF8.GetString(ctx.Payload.ToArray());
}
