using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Endpoints.File;

// Transport config for a File OUTPUT leg: the destination directory and how each file is named.
public sealed class FileOutboundOptions
{
    public CommunicationMode Mode { get; init; } = CommunicationMode.Outbound;
    public string? LogicalEndpointId { get; init; }
    public required string Directory { get; init; }
    public string? FileNameTemplate { get; init; }          // null/blank -> FileNameResolver.DefaultTemplate
    public string DefaultExtension { get; init; } = "txt";  // fills the {ext} token
    public bool CreateDirectory { get; init; } = true;
}

// Transport config for a File INPUT source: which folder to poll and how.
public sealed class FileInboundOptions
{
    public CommunicationMode Mode { get; init; } = CommunicationMode.Inbound;
    public string? LogicalEndpointId { get; init; }
    public required int SourceEndpointId { get; init; }
    public required string Directory { get; init; }
    public string? FilePattern { get; init; }               // ";"-delimited extensions (e.g. "*.hl7;*.txt"); null/blank = all
    public bool Recursive { get; init; }
    public int PollIntervalSeconds { get; init; } = 10;
    public string Format { get; init; } = Abstractions.MessageFormats.Hl7v2;
    public bool KeepOriginalFiles { get; init; }            // false (default) -> move consumed files to processed/error; true -> leave in place, advance .lastProcessedTime (read-only shares)
    public int RetentionDays { get; init; }                 // 0 = keep disposed files forever (no retention sweep of processed/error)
    public string? Username { get; init; }                  // network-share auth (UNC directories); host builds the credential
    public string? Domain { get; init; }
    public string? PasswordProtected { get; init; }         // DPAPI-protected (base64); decrypted by the host at composition
}
