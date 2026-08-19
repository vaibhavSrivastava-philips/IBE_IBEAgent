using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.Endpoints.Http;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class HttpInboundEndpointTests
{
    [Fact]
    public async Task Receives_post_and_writes_reply_as_response_body()
    {
        var prefix = $"http://localhost:{TestSupport.GetFreePort()}/ibe/";
        var dispatcher = new FakeMessageDispatcher();
        var options = new HttpInboundOptions
        {
            SourceEndpointId = 3, Prefix = prefix, Format = "hl7v2", ReplyTimeout = TimeSpan.FromSeconds(10),
        };
        await using var endpoint = new HttpInboundEndpoint(options, dispatcher, new FakeReplyContextFactory());
        await endpoint.StartAsync(CancellationToken.None);

        using var http = new HttpClient();
        var body = Encoding.UTF8.GetBytes("MSH|HTTP");
        var postTask = http.PostAsync(prefix, new ByteArrayContent(body));   // blocks until reply/timeout

        await TestSupport.WaitForAsync(() => dispatcher.Dispatched.Count == 1, TimeSpan.FromSeconds(5));
        var ctx = dispatcher.Dispatched[0];
        Assert.Equal(3, ctx.SourceEndpointId);
        Assert.Equal(body, ctx.Payload.ToArray());

        var reply = Encoding.UTF8.GetBytes("MSA|AA");
        await ctx.Ack.WriteAsync(reply, CancellationToken.None);             // unblocks the HTTP response

        using var resp = await postTask;
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(reply, await resp.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Returns_504_when_no_reply_within_timeout()
    {
        var prefix = $"http://localhost:{TestSupport.GetFreePort()}/ibe/";
        var options = new HttpInboundOptions
        {
            SourceEndpointId = 1, Prefix = prefix, ReplyTimeout = TimeSpan.FromMilliseconds(300),
        };
        await using var endpoint = new HttpInboundEndpoint(options, new FakeMessageDispatcher(), new FakeReplyContextFactory());
        await endpoint.StartAsync(CancellationToken.None);

        using var http = new HttpClient();
        using var resp = await http.PostAsync(prefix, new ByteArrayContent(Encoding.UTF8.GetBytes("x")));

        Assert.Equal(HttpStatusCode.GatewayTimeout, resp.StatusCode);        // 504 (one-shot token also covered here)
    }

    [Fact]
    public async Task Relays_the_request_content_type_into_the_content_type_header_when_enabled()
    {
        var prefix = $"http://localhost:{TestSupport.GetFreePort()}/ibe/";
        var dispatcher = new FakeMessageDispatcher();
        var options = new HttpInboundOptions
        {
            SourceEndpointId = 3, Prefix = prefix, ReplyTimeout = TimeSpan.FromSeconds(10), RelayContentType = true,
        };
        await using var endpoint = new HttpInboundEndpoint(options, dispatcher, new FakeReplyContextFactory());
        await endpoint.StartAsync(CancellationToken.None);

        using var http = new HttpClient();
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("<x/>"));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
        var postTask = http.PostAsync(prefix, content);

        await TestSupport.WaitForAsync(() => dispatcher.Dispatched.Count == 1, TimeSpan.FromSeconds(5));
        var ctx = dispatcher.Dispatched[0];
        Assert.Equal("application/xml", ctx.Headers[ContentHeaders.ContentType]);

        await ctx.Ack.WriteAsync(Encoding.UTF8.GetBytes("ok"), CancellationToken.None);
        using var _ = await postTask;
    }
}