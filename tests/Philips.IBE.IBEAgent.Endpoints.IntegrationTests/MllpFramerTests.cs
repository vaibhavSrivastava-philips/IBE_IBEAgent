using System.Text;
using Philips.IBE.IBEAgent.Endpoints.Tcp;

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class MllpFramerTests
{
    [Fact]
    public async Task Frame_then_read_roundtrips_single_message()
    {
        var payload = Encoding.UTF8.GetBytes("MSH|^~\\&|SRC|FAC");
        using var stream = new MemoryStream(MllpFramer.Frame(payload));

        var messages = await ReadAllAsync(stream);

        Assert.Single(messages);
        Assert.Equal(payload, messages[0]);
    }

    [Fact]
    public async Task Reads_two_back_to_back_messages_in_one_buffer()
    {
        var a = Encoding.UTF8.GetBytes("AAA");
        var b = Encoding.UTF8.GetBytes("BBB");
        var buffer = MllpFramer.Frame(a).Concat(MllpFramer.Frame(b)).ToArray();
        using var stream = new MemoryStream(buffer);

        var messages = await ReadAllAsync(stream);

        Assert.Equal(2, messages.Count);
        Assert.Equal(a, messages[0]);
        Assert.Equal(b, messages[1]);
    }

    [Fact]
    public async Task Ignores_bytes_before_the_start_block()
    {
        var payload = Encoding.UTF8.GetBytes("HELLO");
        var noise = new byte[] { 0x01, 0x09, 0x0D };                 // junk before 0x0B
        var buffer = noise.Concat(MllpFramer.Frame(payload)).ToArray();
        using var stream = new MemoryStream(buffer);

        var messages = await ReadAllAsync(stream);

        Assert.Single(messages);
        Assert.Equal(payload, messages[0]);
    }

    [Fact]
    public async Task Reassembles_a_message_split_across_reads()
    {
        var payload = Encoding.UTF8.GetBytes("SPLIT-ACROSS-MANY-TINY-CHUNKS");
        using var stream = new ChunkedReadStream(MllpFramer.Frame(payload), chunkSize: 3);

        var messages = await ReadAllAsync(stream);

        Assert.Single(messages);
        Assert.Equal(payload, messages[0]);
    }

    [Fact]
    public async Task Empty_stream_yields_nothing()
    {
        using var stream = new MemoryStream([]);
        var messages = await ReadAllAsync(stream);
        Assert.Empty(messages);
    }

    [Fact]
    public async Task Incomplete_frame_at_end_of_stream_is_dropped()
    {
        var incomplete = new byte[] { Mllp.StartBlock }.Concat(Encoding.UTF8.GetBytes("PARTIAL")).ToArray();
        using var stream = new MemoryStream(incomplete);

        var messages = await ReadAllAsync(stream);

        Assert.Empty(messages);
    }

    [Fact]
    public async Task Nested_start_block_drops_corrupt_partial_frame_and_resynchronizes()
    {
        var valid = Encoding.UTF8.GetBytes("VALID");
        var buffer = new byte[] { Mllp.StartBlock }
            .Concat(Encoding.UTF8.GetBytes("CORRUPT"))
            .Concat(MllpFramer.Frame(valid))
            .ToArray();
        using var stream = new MemoryStream(buffer);

        var messages = await ReadAllAsync(stream);

        var message = Assert.Single(messages);
        Assert.Equal(valid, message);
    }

    [Fact]
    public async Task Malformed_terminator_before_new_start_drops_partial_frame_and_resynchronizes()
    {
        var valid = Encoding.UTF8.GetBytes("VALID-AFTER-BAD-FS");
        var buffer = new byte[] { Mllp.StartBlock }
            .Concat(Encoding.UTF8.GetBytes("CORRUPT"))
            .Concat([Mllp.EndBlock1, Mllp.StartBlock])
            .Concat(valid)
            .Concat([Mllp.EndBlock1, Mllp.EndBlock2])
            .ToArray();
        using var stream = new MemoryStream(buffer);

        var messages = await ReadAllAsync(stream);

        var message = Assert.Single(messages);
        Assert.Equal(valid, message);
    }

    private static async Task<List<byte[]>> ReadAllAsync(Stream stream)
    {
        var list = new List<byte[]>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var msg in MllpFramer.ReadMessagesAsync(stream, cts.Token))
            list.Add(msg);
        return list;
    }
}
