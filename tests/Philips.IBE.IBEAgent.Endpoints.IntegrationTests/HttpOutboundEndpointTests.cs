using System.Net;
using System.Text;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.Endpoints.Http;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class HttpOutboundEndpointTests
{
    [Fact]
    public async Task Forwards_only_fwd_prefixed_headers_as_request_headers()
    {
        var handler = new CapturingHandler();
        using var client = new HttpClient(handler);
        var endpoint = new HttpOutboundEndpoint(
            new HttpOutboundOptions { Endpoint = new Uri("http://localhost/ibe/inbound") }, codec: null, client);

        var ctx = new MessageContext("cid", 1, MessageFormats.Hl7v2, new FakeAckToken(), new RecordingReplyContext(),
            payload: Encoding.UTF8.GetBytes("MSH|x"),
            headers: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ForwardHeaders.Key("filesourcepath")] = "adt.hl7",
                [ForwardHeaders.Key("X-Custom")] = "v1",
                ["blob.name"] = "internal-should-not-leak",   // non-fwd internal header stays off the wire
            });

        var result = await endpoint.SendAsync(ctx, CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
        Assert.Equal("cid", handler.Headers[TransportCorrelationHeaders.WireRequestId]);
        Assert.True(handler.Headers.ContainsKey(TransportCorrelationHeaders.WireMessageId));
        Assert.Equal("adt.hl7", handler.Headers["filesourcepath"]);
        Assert.Equal("v1", handler.Headers["X-Custom"]);         // any fwd.* header forwarded (generic, not File-specific)
        Assert.False(handler.Headers.ContainsKey("blob.name"));
    }

    [Fact]
    public async Task Honors_the_content_type_header_over_the_endpoint_default()
    {
        var handler = new CapturingHandler();
        using var client = new HttpClient(handler);
        var endpoint = new HttpOutboundEndpoint(
            new HttpOutboundOptions { Endpoint = new Uri("http://localhost/ibe/inbound"), ContentType = "application/json" },
            codec: null, client);

        var ctx = new MessageContext("cid", 1, MessageFormats.Hl7v2, new FakeAckToken(), new RecordingReplyContext(),
            payload: Encoding.UTF8.GetBytes("%PDF-1.7"),
            headers: new Dictionary<string, string>(StringComparer.Ordinal) { [ContentHeaders.ContentType] = "application/pdf" });

        await endpoint.SendAsync(ctx, CancellationToken.None);

        Assert.Equal("application/pdf", handler.ContentType);
    }

    [Fact]
    public async Task Falls_back_to_the_endpoint_content_type_when_no_header_is_set()
    {
        var handler = new CapturingHandler();
        using var client = new HttpClient(handler);
        var endpoint = new HttpOutboundEndpoint(
            new HttpOutboundOptions { Endpoint = new Uri("http://localhost/ibe/inbound"), ContentType = "application/json" },
            codec: null, client);

        var ctx = new MessageContext("cid", 1, MessageFormats.Hl7v2, new FakeAckToken(), new RecordingReplyContext(),
            payload: Encoding.UTF8.GetBytes("{}"),
            headers: new Dictionary<string, string>(StringComparer.Ordinal));

        await endpoint.SendAsync(ctx, CancellationToken.None);

        Assert.Equal("application/json", handler.ContentType);
    }

    [Fact]
    public async Task DuplexOutbound_http_uses_logical_pair_for_outbound_post_and_inbound_callback()
    {
        var callbackPrefix = $"http://localhost:{TestSupport.GetFreePort()}/ibe/callback/";
        var dispatcher = new FakeMessageDispatcher();
        var replyFactory = new FakeReplyContextFactory();
        await using var inbound = new HttpInboundEndpoint(
            new HttpInboundOptions
            {
                Mode = CommunicationMode.DuplexInbound,
                LogicalEndpointId = "partner-a",
                SourceEndpointId = 33,
                Prefix = callbackPrefix,
                ReplyTimeoutInMs = 10_000,
            },
            dispatcher,
            replyFactory);
        await inbound.StartAsync(CancellationToken.None);

        var handler = new CapturingHandler();
        using var client = new HttpClient(handler);
        var outbound = new HttpOutboundEndpoint(
            new HttpOutboundOptions
            {
                Mode = CommunicationMode.DuplexOutbound,
                LogicalEndpointId = "partner-a",
                Endpoint = new Uri("http://partner.example/ibe/outbound"),
            },
            codec: null,
            client);

        var outboundResult = await outbound.SendAsync(MessageContextBuilder.Create(payload: "OUTBOUND"), CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Delivered, outboundResult.Outcome);
        Assert.Equal("OUTBOUND", Encoding.UTF8.GetString(handler.Body));
        Assert.Equal("partner-a", handler.Headers[TransportCorrelationHeaders.WireLogicalEndpointId]);

        using var http = new HttpClient();
        var callbackBody = Encoding.UTF8.GetBytes("MSH|CALLBACK");
        var callbackTask = http.PostAsync(callbackPrefix, new ByteArrayContent(callbackBody));

        await TestSupport.WaitForAsync(() => dispatcher.Dispatched.Count == 1, TimeSpan.FromSeconds(5));
        var callbackContext = dispatcher.Dispatched[0];
        Assert.Equal(33, callbackContext.SourceEndpointId);
        Assert.Equal(callbackBody, callbackContext.Payload.ToArray());

        var ack = Encoding.UTF8.GetBytes("MSA|AA");
        await callbackContext.Ack.WriteAsync(ack, CancellationToken.None);

        using var callbackResponse = await callbackTask;
        Assert.Equal(HttpStatusCode.OK, callbackResponse.StatusCode);
        Assert.Equal(ack, await callbackResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Inbound_callback_uses_request_id_as_correlation_and_preserves_http_correlation_headers()
    {
        var callbackPrefix = $"http://localhost:{TestSupport.GetFreePort()}/ibe/callback/";
        var dispatcher = new FakeMessageDispatcher();
        await using var inbound = new HttpInboundEndpoint(
            new HttpInboundOptions
            {
                Mode = CommunicationMode.DuplexInbound,
                LogicalEndpointId = "partner-a",
                SourceEndpointId = 33,
                Prefix = callbackPrefix,
                ReplyTimeoutInMs = 10_000,
            },
            dispatcher,
            new FakeReplyContextFactory());
        await inbound.StartAsync(CancellationToken.None);

        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, callbackPrefix)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("MSH|CALLBACK")),
        };
        request.Headers.TryAddWithoutValidation(TransportCorrelationHeaders.WireRequestId, "request-123");
        request.Headers.TryAddWithoutValidation(TransportCorrelationHeaders.WireMessageId, "message-456");
        request.Headers.TryAddWithoutValidation(TransportCorrelationHeaders.WireLogicalEndpointId, "partner-a");
        var responseTask = http.SendAsync(request);

        await TestSupport.WaitForAsync(() => dispatcher.Dispatched.Count == 1, TimeSpan.FromSeconds(5));
        var callbackContext = dispatcher.Dispatched[0];

        Assert.Equal("request-123", callbackContext.CorrelationId);
        Assert.Equal("request-123", callbackContext.Headers[TransportCorrelationHeaders.RequestId]);
        Assert.Equal("message-456", callbackContext.Headers[TransportCorrelationHeaders.MessageId]);
        Assert.Equal("partner-a", callbackContext.Headers[TransportCorrelationHeaders.LogicalEndpointId]);

        await callbackContext.Ack.WriteAsync(Encoding.UTF8.GetBytes("MSA|AA"), CancellationToken.None);
        using var response = await responseTask;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Snapshots the outgoing request headers before the endpoint disposes the request.
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Dictionary<string, string> Headers { get; } = new(StringComparer.Ordinal);
        public byte[] Body { get; private set; } = [];
        public string? ContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            foreach (var header in request.Headers)
                Headers[header.Key] = string.Concat(header.Value);
            ContentType = request.Content?.Headers.ContentType?.ToString();
            Body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        }
    }
}
