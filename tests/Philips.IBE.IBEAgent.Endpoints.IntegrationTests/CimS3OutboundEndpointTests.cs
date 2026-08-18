using System.Net;
using System.Text;
using System.IO.Compression;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Endpoints.CimS3;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class CimS3OutboundEndpointTests
{
    [Fact]
    public async Task Sends_upload_then_notification_as_outbound_workflow()
    {
        var handler = new CapturingHandler();
        using var client = new HttpClient(handler);
        using var endpoint = new CimS3OutboundEndpoint(
            new CimS3OutboundOptions
            {
                PresignedUploadEndpoint = new Uri("https://cim.example/upload"),
                NotificationEndpoint = new Uri("https://cim.example/notify"),
            },
            codec: null,
            client);

        var context = new MessageContext(
            correlationId: "cid-1",
            sourceEndpointId: 1,
            format: MessageFormats.Hl7v2,
            ack: new FakeAckToken(),
            reply: new RecordingReplyContext(),
            payload: Encoding.UTF8.GetBytes("PAYLOAD"));

        var result = await endpoint.SendAsync(context, CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
        Assert.Equal([HttpMethod.Put, HttpMethod.Post], handler.Methods);
        Assert.Equal("PAYLOAD", Encoding.UTF8.GetString(handler.Bodies[0]));
        Assert.Contains("cid-1", Encoding.UTF8.GetString(handler.Bodies[1]));
    }

    [Fact]
    public async Task Acquires_presigned_url_zips_payload_and_sends_idempotency_headers()
    {
        var handler = new CapturingHandler(request =>
        {
            if (request.RequestUri!.AbsoluteUri.EndsWith("/acquire", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"uploadUrl\":\"https://s3.example/presigned\"}", Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        });
        using var client = new HttpClient(handler);
        using var endpoint = new CimS3OutboundEndpoint(
            new CimS3OutboundOptions
            {
                PresignedUploadEndpoint = new Uri("https://fallback.example/upload"),
                PresignedUrlAcquisitionEndpoint = new Uri("https://cim.example/acquire"),
                ZipPayload = true,
                ZipEntryNameTemplate = "{correlationId}.hl7",
            },
            codec: null,
            client);

        var context = new MessageContext(
            correlationId: "cid-zip",
            sourceEndpointId: 1,
            format: MessageFormats.Hl7v2,
            ack: new FakeAckToken(),
            reply: new RecordingReplyContext(),
            payload: Encoding.UTF8.GetBytes("ZIPME"));

        var result = await endpoint.SendAsync(context, CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
        Assert.Equal([HttpMethod.Post, HttpMethod.Put], handler.Methods);
        Assert.Equal("https://s3.example/presigned", handler.Urls[1]);
        Assert.Equal(context.MessageId.ToString("N"), handler.Headers[1]["X-IBE-Idempotency-Key"]);
        Assert.Equal("cid-zip", handler.Headers[1]["X-IBE-Cim-Batch-Id"]);

        using var zip = new ZipArchive(new MemoryStream(handler.Bodies[1]), ZipArchiveMode.Read);
        var entry = Assert.Single(zip.Entries);
        Assert.Equal("cid-zip.hl7", entry.FullName);
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        Assert.Equal("ZIPME", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Splits_zipped_payload_into_multiple_entries_and_notifies_entry_metadata()
    {
        var handler = new CapturingHandler();
        using var client = new HttpClient(handler);
        using var endpoint = new CimS3OutboundEndpoint(
            new CimS3OutboundOptions
            {
                PresignedUploadEndpoint = new Uri("https://cim.example/upload"),
                NotificationEndpoint = new Uri("https://cim.example/notify"),
                ZipPayload = true,
                ZipEntryNameTemplate = "{correlationId}-{index}-of-{entryCount}.avro",
                MaxZipEntryBytes = 4,
            },
            codec: null,
            client);

        var context = new MessageContext(
            correlationId: "cid-chunk",
            sourceEndpointId: 1,
            format: MessageFormats.Hl7v2,
            ack: new FakeAckToken(),
            reply: new RecordingReplyContext(),
            payload: Encoding.UTF8.GetBytes("ABCDEFGHIJ"));

        var result = await endpoint.SendAsync(context, CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
        using var zip = new ZipArchive(new MemoryStream(handler.Bodies[0]), ZipArchiveMode.Read);
        Assert.Equal(["cid-chunk-1-of-3.avro", "cid-chunk-2-of-3.avro", "cid-chunk-3-of-3.avro"], zip.Entries.Select(e => e.FullName).ToArray());
        Assert.Equal("ABCD", await ReadEntryAsync(zip.Entries[0]));
        Assert.Equal("EFGH", await ReadEntryAsync(zip.Entries[1]));
        Assert.Equal("IJ", await ReadEntryAsync(zip.Entries[2]));

        var notification = Encoding.UTF8.GetString(handler.Bodies[1]);
        Assert.Contains("\"sourceByteCount\":10", notification);
        Assert.Contains("\"entryCount\":3", notification);
    }

    [Fact]
    public async Task Returns_failed_when_upload_succeeds_but_notification_fails()
    {
        var handler = new CapturingHandler(request =>
            request.Method == HttpMethod.Post
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler);
        using var endpoint = new CimS3OutboundEndpoint(
            new CimS3OutboundOptions
            {
                PresignedUploadEndpoint = new Uri("https://cim.example/upload"),
                NotificationEndpoint = new Uri("https://cim.example/notify"),
            },
            codec: null,
            client);

        var result = await endpoint.SendAsync(MessageContextBuilder.Create(payload: "PAYLOAD"), CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Failed, result.Outcome);
        Assert.Contains("notification failed", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Returns_failed_upload_error_and_does_not_notify_when_upload_fails()
    {
        var handler = new CapturingHandler(request =>
            request.Method == HttpMethod.Put
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler);
        using var endpoint = new CimS3OutboundEndpoint(
            new CimS3OutboundOptions
            {
                PresignedUploadEndpoint = new Uri("https://cim.example/upload"),
                NotificationEndpoint = new Uri("https://cim.example/notify"),
            },
            codec: null,
            client);

        var result = await endpoint.SendAsync(MessageContextBuilder.Create(payload: "PAYLOAD"), CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Failed, result.Outcome);
        Assert.Contains("upload failed", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([HttpMethod.Put], handler.Methods);
    }

    [Fact]
    public async Task Returns_failed_when_presigned_url_acquisition_response_is_invalid()
    {
        var handler = new CapturingHandler(request =>
            request.RequestUri!.AbsoluteUri.EndsWith("/acquire", StringComparison.OrdinalIgnoreCase)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler);
        using var endpoint = new CimS3OutboundEndpoint(
            new CimS3OutboundOptions
            {
                PresignedUploadEndpoint = new Uri("https://fallback.example/upload"),
                PresignedUrlAcquisitionEndpoint = new Uri("https://cim.example/acquire"),
            },
            codec: null,
            client);

        var result = await endpoint.SendAsync(MessageContextBuilder.Create(payload: "PAYLOAD"), CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Failed, result.Outcome);
        Assert.Contains("uploadUrl", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([HttpMethod.Post], handler.Methods);
    }

    [Fact]
    public void Rejects_duplex_modes_because_cim_s3_is_an_outbound_workflow()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new CimS3OutboundEndpoint(
            new CimS3OutboundOptions
            {
                Mode = CommunicationMode.DuplexOutbound,
                PresignedUploadEndpoint = new Uri("https://cim.example/upload"),
            },
            codec: null));

        Assert.Contains("outbound workflow", ex.Message);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage>? responseFactory = null)
        {
            _responseFactory = responseFactory ?? (_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) });
        }

        public List<HttpMethod> Methods { get; } = [];
        public List<string> Urls { get; } = [];
        public List<byte[]> Bodies { get; } = [];
        public List<Dictionary<string, string>> Headers { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            Urls.Add(request.RequestUri!.AbsoluteUri);
            Bodies.Add(request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken));
            Headers.Add(request.Headers.ToDictionary(h => h.Key, h => string.Concat(h.Value), StringComparer.Ordinal));
            return _responseFactory(request);
        }
    }

    private static async Task<string> ReadEntryAsync(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}
