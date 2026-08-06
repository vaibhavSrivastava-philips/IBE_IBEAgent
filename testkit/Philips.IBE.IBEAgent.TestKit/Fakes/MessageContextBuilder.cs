using System.Text;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.TestKit;

public static class MessageContextBuilder
{
    public static MessageContext Create(
        int sourceEndpointId = 1,
        string format = MessageFormats.Hl7v2,
        string payload = "",
        IAckToken? ack = null,
        IReplyContext? reply = null,
        IDictionary<string, string>? headers = null)
        => new(
            correlationId: Guid.NewGuid().ToString("N"),
            sourceEndpointId: sourceEndpointId,
            format: format,
            ack: ack ?? new FakeAckToken(),
            reply: reply ?? new RecordingReplyContext(),
            payload: Encoding.UTF8.GetBytes(payload),
            headers: headers);
}