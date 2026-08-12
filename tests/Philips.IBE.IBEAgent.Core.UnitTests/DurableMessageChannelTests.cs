using System.Text;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class DurableMessageChannelTests
{
    [Fact]
    public async Task Enqueue_persists_then_removes_record_when_read()
    {
        var dir = CreateTempDir();
        try
        {
            var channel = new DurableMessageChannel(8, dir);
            await channel.EnqueueAsync(MessageContextBuilder.Create(payload: "A"), CancellationToken.None);

            Assert.Equal(1, channel.PersistedCount);
            channel.Complete();

            var payloads = new List<string>();
            await foreach (var context in channel.ReadAllAsync(CancellationToken.None))
                payloads.Add(Encoding.UTF8.GetString(context.Payload.ToArray()));

            Assert.Equal(["A"], payloads);
            Assert.Equal(0, channel.PersistedCount);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Factory_uses_durable_channel_for_spill_to_disk_or_at_least_once()
    {
        var dir = CreateTempDir();
        try
        {
            var factory = new MessageChannelFactory(dir);

            var spill = factory.Create(new ChannelOptions { OverflowPolicy = OverflowPolicy.SpillToDisk }, "spill", durable: false);
            var durable = factory.Create(new ChannelOptions(), "atleastonce", durable: true);
            var memory = factory.Create(new ChannelOptions(), "memory", durable: false);

            Assert.IsType<DurableMessageChannel>(spill);
            Assert.IsType<DurableMessageChannel>(durable);
            Assert.IsType<BoundedInMemoryChannel>(memory);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ibe-durable-channel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
