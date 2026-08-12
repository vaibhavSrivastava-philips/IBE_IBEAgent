using System.Net.Http.Json;
using System.Text.Json;
using System.IO.Compression;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Endpoints.CimS3;

public sealed class CimS3OutboundEndpoint : IOutboundEndpoint, IDisposable
{
    private readonly CimS3OutboundOptions _options;
    private readonly IMessageCodec? _codec;
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public CimS3OutboundEndpoint(CimS3OutboundOptions options, IMessageCodec? codec, HttpClient? http = null)
    {
        if (options.Mode != CommunicationMode.Outbound)
            throw new InvalidOperationException("CIM S3 is an outbound workflow and does not support duplex transport modes.");

        _options = options;
        _codec = codec;
        _http = http ?? new HttpClient { Timeout = options.Timeout };
        _ownsClient = http is null;
    }

    public async Task<DeliveryResult> SendAsync(MessageContext context, CancellationToken cancellationToken)
    {
        try
        {
            var sourcePayload = _codec?.Encode(context) ?? context.Payload;
            var package = _options.ZipPayload
                ? await ZipAsync(sourcePayload, context, cancellationToken)
                : new CimS3PayloadPackage(sourcePayload.ToArray(), 1, sourcePayload.Length);
            var uploadEndpoint = await ResolveUploadEndpointAsync(context, cancellationToken);

            using var content = new ByteArrayContent(package.Payload);
            content.Headers.TryAddWithoutValidation("Content-Type", _options.ContentType);

            using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, uploadEndpoint) { Content = content };
            AddWorkflowHeaders(uploadRequest, context);

            using var uploadResponse = await _http.SendAsync(uploadRequest, cancellationToken);
            if (!uploadResponse.IsSuccessStatusCode)
                return new DeliveryResult(DeliveryOutcome.Failed, $"CIM S3 upload failed: HTTP {(int)uploadResponse.StatusCode}");

            if (_options.NotificationEndpoint is not null)
            {
                var notification = new CimS3UploadNotification(
                    context.CorrelationId,
                    context.MessageId,
                    package.Payload.Length,
                    package.SourceByteCount,
                    package.EntryCount,
                    uploadEndpoint);
                using var notificationRequest = new HttpRequestMessage(HttpMethod.Post, _options.NotificationEndpoint)
                {
                    Content = JsonContent.Create(notification),
                };
                AddWorkflowHeaders(notificationRequest, context);

                using var notificationResponse = await _http.SendAsync(notificationRequest, cancellationToken);
                if (!notificationResponse.IsSuccessStatusCode)
                    return new DeliveryResult(DeliveryOutcome.Failed, $"CIM S3 notification failed: HTTP {(int)notificationResponse.StatusCode}");
            }

            return new DeliveryResult(DeliveryOutcome.Delivered);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new DeliveryResult(DeliveryOutcome.Failed, ex.Message);
        }
    }

    private async Task<Uri> ResolveUploadEndpointAsync(MessageContext context, CancellationToken cancellationToken)
    {
        if (_options.PresignedUrlAcquisitionEndpoint is null)
            return _options.PresignedUploadEndpoint;

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.PresignedUrlAcquisitionEndpoint)
        {
            Content = JsonContent.Create(new CimS3PresignedUrlRequest(context.CorrelationId, context.MessageId)),
        };
        AddWorkflowHeaders(request, context);

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var parsed = JsonSerializer.Deserialize<CimS3PresignedUrlResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (parsed?.UploadUrl is null)
            throw new HttpRequestException("CIM S3 presigned URL acquisition response did not contain uploadUrl.");

        return parsed.UploadUrl;
    }

    private async Task<CimS3PayloadPackage> ZipAsync(ReadOnlyMemory<byte> payload, MessageContext context, CancellationToken cancellationToken)
    {
        var entrySize = _options.MaxZipEntryBytes > 0 ? _options.MaxZipEntryBytes : payload.Length;
        var entryCount = payload.Length == 0 ? 1 : (int)Math.Ceiling(payload.Length / (double)entrySize);
        await using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var index = 0; index < entryCount; index++)
            {
                var offset = index * entrySize;
                var count = Math.Min(entrySize, payload.Length - offset);
                var entryName = ResolveZipEntryName(context, index, entryCount);
                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(payload.Slice(offset, count), cancellationToken);
            }
        }

        return new CimS3PayloadPackage(output.ToArray(), entryCount, payload.Length);
    }

    private string ResolveZipEntryName(MessageContext context, int zeroBasedIndex, int entryCount)
    {
        var templateHasIndex = _options.ZipEntryNameTemplate.Contains("{index}", StringComparison.OrdinalIgnoreCase);
        var entryName = _options.ZipEntryNameTemplate
            .Replace("{correlationId}", context.CorrelationId, StringComparison.OrdinalIgnoreCase)
            .Replace("{messageId}", context.MessageId.ToString("N"), StringComparison.OrdinalIgnoreCase)
            .Replace("{index}", (zeroBasedIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{entryCount}", entryCount.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

        if (entryCount <= 1 || templateHasIndex)
            return entryName;

        var extension = Path.GetExtension(entryName);
        var prefix = string.IsNullOrEmpty(extension) ? entryName : entryName[..^extension.Length];
        return $"{prefix}.{zeroBasedIndex + 1:D4}{extension}";
    }

    private void AddWorkflowHeaders(HttpRequestMessage request, MessageContext context)
    {
        if (!_options.IncludeIdempotencyHeaders) return;
        request.Headers.TryAddWithoutValidation(_options.IdempotencyHeaderName, context.MessageId.ToString("N"));
        request.Headers.TryAddWithoutValidation(_options.BatchIdHeaderName, context.CorrelationId);
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }

    private sealed record CimS3PresignedUrlRequest(string CorrelationId, Guid MessageId);
    private sealed record CimS3PresignedUrlResponse(Uri UploadUrl);
    private sealed record CimS3PayloadPackage(byte[] Payload, int EntryCount, int SourceByteCount);
    private sealed record CimS3UploadNotification(string CorrelationId, Guid MessageId, int ByteCount, int SourceByteCount, int EntryCount, Uri UploadUrl);
}
