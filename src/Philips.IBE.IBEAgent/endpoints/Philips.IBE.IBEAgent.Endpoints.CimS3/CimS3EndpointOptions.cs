using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Endpoints.CimS3;

public sealed class CimS3OutboundOptions
{
    public CommunicationMode Mode { get; init; } = CommunicationMode.Outbound;
    public required Uri PresignedUploadEndpoint { get; init; }
    public Uri? PresignedUrlAcquisitionEndpoint { get; init; }
    public Uri? NotificationEndpoint { get; init; }
    public string ContentType { get; init; } = "application/octet-stream";
    public bool ZipPayload { get; init; }
    public string ZipEntryNameTemplate { get; init; } = "{correlationId}.bin";
    public int MaxZipEntryBytes { get; init; }
    public bool IncludeIdempotencyHeaders { get; init; } = true;
    public string IdempotencyHeaderName { get; init; } = "X-IBE-Idempotency-Key";
    public string BatchIdHeaderName { get; init; } = "X-IBE-Cim-Batch-Id";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
