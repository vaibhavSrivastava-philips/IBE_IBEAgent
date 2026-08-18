using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.Endpoints.File;
using Philips.IBE.IBEAgent.Endpoints.Http;
using Philips.IBE.IBEAgent.Endpoints.Tcp;
using Philips.IBE.IBEAgent.Endpoints.WebSocket;
using Philips.IBE.IBEAgent.Formats.Hl7;
using Philips.IBE.IBEAgent.Formats.Hl7.Cim;
using Microsoft.Extensions.Logging;

namespace Philips.IBE.IBEAgent.Service;

// §3.10 — composition-time registration: builds the ComponentRegistry with the concrete stages,
// codecs, and outbound endpoint factories this deployment supports. New protocols/codecs are
// added here (register + name), never by editing Core.
public static class ComponentRegistryBuilder
{
    public static ComponentRegistry Build(AgentEndpointsOptions endpoints, CatalogOptions catalog, ILoggerFactory loggerFactory, TcpDuplexSessionRegistry? tcpDuplexSessions = null, WebSocketDuplexSessionRegistry? webSocketDuplexSessions = null)
    {
        var registry = new ComponentRegistry();

        // §3.10 — generic Core stages (name -> factory). Module-owned so the host stays thin (OCP);
        // protocol modules register their own stages the same way when they gain any.
        registry.AddCoreStages();
        registry.AddHl7Stages(loggerFactory);   // protocol (HL7) stages, e.g. hl7-classify

        // §3.8/§6 — Enhanced-ack rendering: HL7's own (Format x Shape) generated ack.
        var hl7SingleAckFormatter = new Hl7SingleAckFormatter(loggerFactory.CreateLogger<Hl7SingleAckFormatter>());
        registry.RegisterAckFormatter(hl7SingleAckFormatter);
        registry.RegisterAckFormatter(new Hl7BatchAckFormatter(hl7SingleAckFormatter));

        // §3.10 — register each configured codec by its Type (the key CreateMessageCodec resolves by),
        // so every outbound leg draws its codec from here: hl7v2 (pass-through) or base64 (file content).
        foreach (var codec in catalog.Codecs.Values)
        {
            if (codec.Type == "hl7v2")
                registry.RegisterMessageCodec(codec.Type, _ => new Hl7v2Codec());
            else if (codec.Type == "cim-json")
                registry.RegisterMessageCodec(codec.Type, _ => new CimJsonCodec());
            else if (codec.Type == "cim-avro")
                registry.RegisterMessageCodec(codec.Type, _ => new CimAvroCodec());
            else if (codec.Type == "base64")
                registry.RegisterMessageCodec(codec.Type, _ => new Base64Codec());
        }

        foreach (var tcp in endpoints.TcpOutbound)
        {
            registry.RegisterOutboundEndpoint(tcp.OutputId, output => new TcpOutboundEndpoint(
                new TcpOutboundOptions
                {
                    Mode = tcp.Mode,
                    SourceEndpointId = tcp.SourceEndpointId,
                    DuplexInboundSourceEndpointId = tcp.DuplexInboundSourceEndpointId,
                    InboundFormat = tcp.InboundFormat,
                    ReplyCorrelationTimeout = TimeSpan.FromSeconds(tcp.ReplyCorrelationTimeoutSeconds),
                    ReconnectDelay = TimeSpan.FromSeconds(tcp.ReconnectDelaySeconds),
                    Host = tcp.Host,
                    Port = tcp.Port,
                    PoolSize = tcp.PoolSize,
                    ExpectReply = tcp.ExpectReply,
                    Ssl = tcp.Ssl,
                    Proxy = tcp.Proxy,
                },
                ResolveCodec(registry, catalog, output.Encoding),
                loggerFactory.CreateLogger<TcpOutboundEndpoint>(),
                tcpDuplexSessions));
        }

        foreach (var http in endpoints.HttpOutbound)
        {
            registry.RegisterOutboundEndpoint(http.OutputId, output => new HttpOutboundEndpoint(
                new HttpOutboundOptions
                {
                    Mode = http.Mode,
                    LogicalEndpointId = http.LogicalEndpointId,
                    Endpoint = http.Endpoint,
                    ContentType = http.ContentType,
                    Timeout = TimeSpan.FromSeconds(http.TimeoutSeconds),
                    MaxConnectionsPerServer = http.MaxConnectionsPerServer,
                    PooledConnectionLifetime = TimeSpan.FromSeconds(http.PooledConnectionLifetimeSeconds),
                    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(http.PooledConnectionIdleTimeoutSeconds),
                    Ssl = http.Ssl,
                    Proxy = http.Proxy,
                },
                ResolveCodec(registry, catalog, output.Encoding)));
        }

        foreach (var file in endpoints.FileOutbound)
        {
            registry.RegisterOutboundEndpoint(file.OutputId, output => new FileOutboundEndpoint(
                new FileOutboundOptions
                {
                    Mode = file.Mode,
                    LogicalEndpointId = file.LogicalEndpointId,
                    Directory = file.Directory,
                    FileNameTemplate = file.FileNameTemplate,
                    DefaultExtension = file.DefaultExtension,
                },
                ResolveCodec(registry, catalog, output.Encoding)));
        }

        foreach (var ws in endpoints.WebSocketOutbound)
        {
            registry.RegisterOutboundEndpoint(ws.OutputId, output => new WebSocketOutboundEndpoint(
                new WebSocketOutboundOptions
                {
                    Mode = ws.Mode,
                    SourceEndpointId = ws.SourceEndpointId,
                    DuplexInboundSourceEndpointId = ws.DuplexInboundSourceEndpointId,
                    InboundFormat = ws.InboundFormat,
                    Endpoint = ws.Endpoint,
                    ExpectReply = ws.ExpectReply,
                    ReplyCorrelationTimeout = TimeSpan.FromSeconds(ws.ReplyCorrelationTimeoutSeconds),
                    ReconnectDelay = TimeSpan.FromSeconds(ws.ReconnectDelaySeconds),
                    PoolSize = ws.PoolSize,
                    ReceiveBufferSize = ws.ReceiveBufferSize,
                    Ssl = ws.Ssl,
                    Proxy = ws.Proxy,
                },
                output.Encoding is { } wsEncoding
                    && catalog.Codecs.TryGetValue(wsEncoding, out var codecOptions) && codecOptions.Type == "hl7v2"
                    ? new Hl7v2Codec()
                    : null,
                webSocketDuplexSessions));
        }

        return registry;
    }

    // §3.10 — resolve a leg's wire codec by name through the registry (register-by-Type/resolve-by-Type),
    // so hl7v2/base64/any future codec flows from one place. A null Encoding (or a name absent from the
    // catalog) means send the canonical payload unencoded.
    private static IMessageCodec? ResolveCodec(ComponentRegistry registry, CatalogOptions catalog, string? encoding)
        => encoding is { } name && catalog.Codecs.TryGetValue(name, out var codecOptions)
            ? registry.CreateMessageCodec(name, codecOptions)
            : null;
}
