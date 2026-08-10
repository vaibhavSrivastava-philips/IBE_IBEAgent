using System.Net;
using System.Text;
using Philips.IBE.IBEAgent.Abstractions;
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
        Assert.Equal("adt.hl7", handler.Headers["filesourcepath"]);
        Assert.Equal("v1", handler.Headers["X-Custom"]);         // any fwd.* header forwarded (generic, not File-specific)
        Assert.False(handler.Headers.ContainsKey("blob.name"));
    }

    // Snapshots the outgoing request headers before the endpoint disposes the request.
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Dictionary<string, string> Headers { get; } = new(StringComparer.Ordinal);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            foreach (var header in request.Headers)
                Headers[header.Key] = string.Concat(header.Value);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) });
        }
    }
}
