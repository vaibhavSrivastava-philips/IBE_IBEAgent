using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;
using Philips.IBE.IBEAgent.Security;

namespace Philips.IBE.IBEAgent.Service;

public static class AgentEndpointsOptionsValidator
{
    public static ValidationResult Validate(AgentEndpointsOptions endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var result = ValidationResult.Success();

        foreach (var endpoint in endpoints.TcpInbound)
        {
            ValidateInbound("TcpInbound", endpoint.SourceEndpointId, endpoint.Mode, result);
            ValidateTcpInboundTls("TcpInbound", endpoint.SourceEndpointId, endpoint.Tls, result);
        }
        foreach (var endpoint in endpoints.HttpInbound)
        {
            ValidateInbound("HttpInbound", endpoint.SourceEndpointId, endpoint.Mode, result);
            ValidateHttpListenerTls("HttpInbound", endpoint.SourceEndpointId, endpoint.Prefix, endpoint.Tls, result);
        }
        foreach (var endpoint in endpoints.WebSocketInbound)
        {
            ValidateInbound("WebSocketInbound", endpoint.SourceEndpointId, endpoint.Mode, result);
            ValidateHttpListenerTls("WebSocketInbound", endpoint.SourceEndpointId, endpoint.Prefix, endpoint.Tls, result);
        }
        foreach (var endpoint in endpoints.FileInbound)
            ValidateInbound("FileInbound", endpoint.SourceEndpointId, endpoint.Mode, result);

        foreach (var endpoint in endpoints.TcpOutbound)
        {
            ValidateTcpOutbound(endpoint, result);
            if (endpoint.Mode == CommunicationMode.DuplexOutbound && endpoint.SourceEndpointId is null or <= 0)
                result.AddError($"TcpOutbound OutputId {endpoint.OutputId} uses DuplexOutbound but has no SourceEndpointId configured.");
            if (endpoint.Mode == CommunicationMode.DuplexInbound && endpoint.DuplexInboundSourceEndpointId is null or <= 0)
                result.AddError($"TcpOutbound OutputId {endpoint.OutputId} uses DuplexInbound but has no DuplexInboundSourceEndpointId configured.");
            ValidateOutboundTls("TcpOutbound", endpoint.OutputId, endpoint.Tls, result);
        }
        foreach (var endpoint in endpoints.HttpOutbound)
        {
            ValidateOutbound("HttpOutbound", endpoint.OutputId, endpoint.Mode, result);
            ValidateHttpLogicalPair(endpoint, endpoints.HttpInbound, result);
            ValidateOutboundTls("HttpOutbound", endpoint.OutputId, endpoint.Tls, result);
            ValidateHttpOutboundTlsUri(endpoint, result);
        }
        foreach (var endpoint in endpoints.WebSocketOutbound)
        {
            ValidateWebSocketOutbound(endpoint, result);
            if (endpoint.Mode == CommunicationMode.DuplexOutbound && endpoint.SourceEndpointId is null or <= 0)
                result.AddError($"WebSocketOutbound OutputId {endpoint.OutputId} uses DuplexOutbound but has no SourceEndpointId configured.");
            if (endpoint.Mode == CommunicationMode.DuplexInbound && endpoint.DuplexInboundSourceEndpointId is null or <= 0)
                result.AddError($"WebSocketOutbound OutputId {endpoint.OutputId} uses DuplexInbound but has no DuplexInboundSourceEndpointId configured.");
            ValidateOutboundTls("WebSocketOutbound", endpoint.OutputId, endpoint.Tls, result);
            ValidateWebSocketOutboundTlsUri(endpoint, result);
        }
        foreach (var endpoint in endpoints.FileOutbound)
        {
            ValidateOutbound("FileOutbound", endpoint.OutputId, endpoint.Mode, result);
            ValidateFileLogicalPair(endpoint, endpoints.FileInbound, result);
        }

        return result;
    }

    private static void ValidateInbound(string sectionName, int sourceEndpointId, CommunicationMode mode, ValidationResult result)
    {
        if (sourceEndpointId <= 0)
            result.AddError($"{sectionName} SourceEndpointId must be > 0.");

        if (mode is not (CommunicationMode.Inbound or CommunicationMode.DuplexInbound))
        {
            result.AddError(
                $"{sectionName} SourceEndpointId {sourceEndpointId} uses invalid Mode '{mode}'. Inbound sections support Inbound or DuplexInbound only.");
        }
    }

    private static void ValidateTcpOutbound(TcpOutboundEndpointConfig endpoint, ValidationResult result)
    {
        if (endpoint.OutputId <= 0)
            result.AddError("TcpOutbound OutputId must be > 0.");

        if (endpoint.Mode is not (CommunicationMode.Outbound or CommunicationMode.DuplexOutbound or CommunicationMode.DuplexInbound))
        {
            result.AddError(
                $"TcpOutbound OutputId {endpoint.OutputId} uses invalid Mode '{endpoint.Mode}'. TcpOutbound supports Outbound, DuplexOutbound, or DuplexInbound.");
        }
    }

    private static void ValidateWebSocketOutbound(WebSocketOutboundEndpointConfig endpoint, ValidationResult result)
    {
        if (endpoint.OutputId <= 0)
            result.AddError("WebSocketOutbound OutputId must be > 0.");

        if (endpoint.Mode is not (CommunicationMode.Outbound or CommunicationMode.DuplexOutbound or CommunicationMode.DuplexInbound))
        {
            result.AddError(
                $"WebSocketOutbound OutputId {endpoint.OutputId} uses invalid Mode '{endpoint.Mode}'. WebSocketOutbound supports Outbound, DuplexOutbound, or DuplexInbound.");
        }
    }

    private static void ValidateOutbound(string sectionName, int outputId, CommunicationMode mode, ValidationResult result)
    {
        if (outputId <= 0)
            result.AddError($"{sectionName} OutputId must be > 0.");

        if (mode is not (CommunicationMode.Outbound or CommunicationMode.DuplexOutbound))
        {
            result.AddError(
                $"{sectionName} OutputId {outputId} uses invalid Mode '{mode}'. Outbound sections support Outbound or DuplexOutbound only.");
        }
    }

    private static void ValidateHttpLogicalPair(HttpOutboundEndpointConfig endpoint, IReadOnlyList<Endpoints.Http.HttpInboundOptions> inboundEndpoints, ValidationResult result)
    {
        if (endpoint.Mode != CommunicationMode.DuplexOutbound)
            return;

        if (string.IsNullOrWhiteSpace(endpoint.LogicalEndpointId))
        {
            result.AddError($"HttpOutbound OutputId {endpoint.OutputId} uses DuplexOutbound but has no LogicalEndpointId configured.");
            return;
        }

        var matches = inboundEndpoints.Where(i => string.Equals(i.LogicalEndpointId, endpoint.LogicalEndpointId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0)
        {
            result.AddError($"HttpOutbound OutputId {endpoint.OutputId} uses DuplexOutbound logical endpoint '{endpoint.LogicalEndpointId}' but no matching HttpInbound endpoint is configured.");
            return;
        }

        if (matches.All(i => i.Mode != CommunicationMode.DuplexInbound))
        {
            result.AddError($"HttpOutbound OutputId {endpoint.OutputId} uses DuplexOutbound logical endpoint '{endpoint.LogicalEndpointId}' but the matching HttpInbound endpoint is not DuplexInbound.");
        }
    }

    private static void ValidateFileLogicalPair(FileOutboundEndpointConfig endpoint, IReadOnlyList<Endpoints.File.FileInboundOptions> inboundEndpoints, ValidationResult result)
    {
        if (endpoint.Mode != CommunicationMode.DuplexOutbound)
            return;

        if (string.IsNullOrWhiteSpace(endpoint.LogicalEndpointId))
        {
            result.AddError($"FileOutbound OutputId {endpoint.OutputId} uses DuplexOutbound but has no LogicalEndpointId configured.");
            return;
        }

        var matches = inboundEndpoints.Where(i => string.Equals(i.LogicalEndpointId, endpoint.LogicalEndpointId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0)
        {
            result.AddError($"FileOutbound OutputId {endpoint.OutputId} uses DuplexOutbound logical endpoint '{endpoint.LogicalEndpointId}' but no matching FileInbound endpoint is configured.");
            return;
        }

        if (matches.All(i => i.Mode != CommunicationMode.DuplexInbound))
        {
            result.AddError($"FileOutbound OutputId {endpoint.OutputId} uses DuplexOutbound logical endpoint '{endpoint.LogicalEndpointId}' but the matching FileInbound endpoint is not DuplexInbound.");
        }
    }

    private static void ValidateTcpInboundTls(string sectionName, int sourceEndpointId, TlsOptions tls, ValidationResult result)
    {
        ValidateCommonTls(sectionName, sourceEndpointId, tls, result);
        if (tls.IsEnabled && !tls.HasCertificate())
        {
            result.AddError($"{sectionName} SourceEndpointId {sourceEndpointId} enables TLS but has no local/server certificate reference.");
        }
    }

    private static void ValidateHttpListenerTls(string sectionName, int sourceEndpointId, string prefix, TlsOptions tls, ValidationResult result)
    {
        ValidateCommonTls(sectionName, sourceEndpointId, tls, result);
        if (tls.IsEnabled && !prefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            result.AddError($"{sectionName} SourceEndpointId {sourceEndpointId} enables TLS but Prefix '{prefix}' is not https://.");
        }
    }

    private static void ValidateOutboundTls(string sectionName, int outputId, TlsOptions tls, ValidationResult result)
    {
        ValidateCommonTls(sectionName, outputId, tls, result);
    }

    private static void ValidateHttpOutboundTlsUri(HttpOutboundEndpointConfig endpoint, ValidationResult result)
    {
        if (endpoint.Tls.IsEnabled && !string.Equals(endpoint.Endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            result.AddError($"HttpOutbound OutputId {endpoint.OutputId} enables TLS but Endpoint '{endpoint.Endpoint}' is not https://.");
        }
    }

    private static void ValidateWebSocketOutboundTlsUri(WebSocketOutboundEndpointConfig endpoint, ValidationResult result)
    {
        if (endpoint.Tls.IsEnabled && !string.Equals(endpoint.Endpoint.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
        {
            result.AddError($"WebSocketOutbound OutputId {endpoint.OutputId} enables TLS but Endpoint '{endpoint.Endpoint}' is not wss://.");
        }
    }

    private static void ValidateCommonTls(string sectionName, int endpointId, TlsOptions tls, ValidationResult result)
    {
        ValidateCertificateReference(sectionName, endpointId, "Certificate", tls.Certificate, result);
        ValidateCertificateReference(sectionName, endpointId, "RootCertificate", tls.RootCertificate, result);

        if (tls.Enabled == false && (tls.HasCertificate() || tls.RequireClientCertificate || tls.RootCertificate is not null))
        {
            result.AddError($"{sectionName} endpoint {endpointId} disables TLS but also configures certificate/trust material.");
        }

        if (tls.SkipCertificateValidation)
        {
            result.AddError($"{sectionName} endpoint {endpointId} enables SkipCertificateValidation, which is not permitted for production-safe TLS configuration.");
        }

        if (tls.RequiresClientCertificate() && !tls.SkipCertificateValidation && !tls.HasRootCertificate())
        {
            result.AddError($"{sectionName} endpoint {endpointId} requires client certificates but has no trusted root certificate authority configured.");
        }
    }

    private static void ValidateCertificateReference(string sectionName, int endpointId, string role, CertificateReference? reference, ValidationResult result)
    {
        if (reference is null)
            return;

        if (string.IsNullOrWhiteSpace(reference.Thumbprint)
            && string.IsNullOrWhiteSpace(reference.Subject)
            && string.IsNullOrWhiteSpace(reference.FriendlyName))
            result.AddError($"{sectionName} endpoint {endpointId} {role} certificate reference must specify at least one of: Subject, Thumbprint, or FriendlyName.");
    }
}
