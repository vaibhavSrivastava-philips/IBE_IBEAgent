using System.Text;
using System.Text.Json;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class TransportMessageEnvelopeTests
{
    [Fact]
    public void ParseJson_returns_raw_payload_for_malformed_envelope()
    {
        var payload = Encoding.UTF8.GetBytes("{not-json");

        var envelope = TransportMessageEnvelope.ParseJson(payload, requireJsonObjectPrefix: false);

        Assert.Null(envelope.CorrelationId);
        Assert.Null(envelope.Headers);
        Assert.Equal(payload, envelope.Payload);
    }

    [Fact]
    public void ParseJson_supports_base64_payload_and_generic_correlation_headers()
    {
        var source = Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray();
        var json = JsonSerializer.Serialize(new
        {
            correlationId = "large-correlation",
            requestId = "large-request",
            messageId = "large-message",
            logicalEndpointId = "large-logical",
            payloadBase64 = Convert.ToBase64String(source),
        });

        var envelope = TransportMessageEnvelope.ParseJson(Encoding.UTF8.GetBytes(json));

        Assert.Equal("large-correlation", envelope.CorrelationId);
        Assert.Equal(source, envelope.Payload);
        Assert.Equal("large-request", envelope.Headers![TransportCorrelationHeaders.RequestId]);
        Assert.Equal("large-message", envelope.Headers[TransportCorrelationHeaders.MessageId]);
        Assert.Equal("large-logical", envelope.Headers[TransportCorrelationHeaders.LogicalEndpointId]);
    }
}
