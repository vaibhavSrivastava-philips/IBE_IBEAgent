using System.Text;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class NormalAckStrategyTests
{
    private static NormalAckStrategy Create(ComponentRegistry? registry = null)
        => new(registry ?? new ComponentRegistry(), AckShape.Single);

    [Fact]
    public void Replies_on_receipt() => Assert.True(Create().RepliesOnReceipt);

    [Fact]
    public async Task Falls_back_to_fixed_ack_when_no_formatter_registered()
    {
        var token = new FakeAckToken();
        var ctx = MessageContextBuilder.Create(ack: token);

        await Create().WriteReplyAsync(ctx, ReplyOutcome.Received());

        Assert.Equal(1, token.WriteCount);
        Assert.Equal("IBE:ACK (no ack formatter)", Encoding.UTF8.GetString(token.Writes[0]));
    }

    [Fact]
    public async Task Renders_via_registered_formatter()
    {
        var token = new FakeAckToken();
        var ctx = MessageContextBuilder.Create(ack: token);
        var registry = new ComponentRegistry().RegisterAckFormatter(new FixedFormatter());

        await Create(registry).WriteReplyAsync(ctx, ReplyOutcome.Received());

        Assert.Equal(1, token.WriteCount);
        Assert.Equal("FORMATTED", Encoding.UTF8.GetString(token.Writes[0]));
    }

    // A stand-in formatter registered under (hl7v2, Single) — proves NormalAck consults the registry.
    private sealed class FixedFormatter : IAckFormatter
    {
        public string Format => MessageFormats.Hl7v2;
        public AckShape Shape => AckShape.Single;
        public ReadOnlyMemory<byte> Render(MessageContext context, in DeliveryResult result)
            => Encoding.UTF8.GetBytes("FORMATTED");
    }
}
