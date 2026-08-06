using Philips.IBE.IBEAgent.Configuration;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.Endpoints.Http;
using Philips.IBE.IBEAgent.Endpoints.Tcp;
using Philips.IBE.IBEAgent.Endpoints.WebSocket;
using Philips.IBE.IBEAgent.Formats.Hl7;
using Microsoft.Extensions.Logging;

namespace Philips.IBE.IBEAgent.Service;

// §3.10 — composition-time registration: builds the ComponentRegistry with the concrete stages,
// codecs, and outbound endpoint factories this deployment supports. New protocols/codecs are
// added here (register + name), never by editing Core.
public static class ComponentRegistryBuilder
{
    public static ComponentRegistry Build(AgentEndpointsOptions endpoints, CatalogOptions catalog, ILoggerFactory loggerFactory)
    {
        var registry = new ComponentRegistry();

        // §3.10 — generic Core stages (name -> factory). Module-owned so the host stays thin (OCP);
        // protocol modules register their own stages the same way when they gain any.
        registry.AddCoreStages();

        // §3.8/§6 — Enhanced-ack rendering: HL7's own (Format x Shape) generated ack.
        registry.RegisterAckFormatter(new Hl7SingleAckFormatter(loggerFactory.CreateLogger<Hl7SingleAckFormatter>()));

        // Codecs: any catalog entry whose Type is "hl7v2" resolves to the pass-through HL7 codec.
        foreach (var (name, codec) in catalog.Codecs)
        {
            if (codec.Type == "hl7v2")
                registry.RegisterMessageCodec(name, _ => new Hl7v2Codec());
        }

        foreach (var tcp in endpoints.TcpOutbound)
        {
            registry.RegisterOutboundEndpoint(tcp.OutputId, output => new TcpOutboundEndpoint(
                new TcpOutboundOptions
                {
                    Host = tcp.Host,
                    Port = tcp.Port,
                    PoolSize = tcp.PoolSize,
                    ExpectReply = tcp.ExpectReply,
                    Ssl = tcp.Ssl,
                    Proxy = tcp.Proxy,
                },
                output.Encoding is { } tcpEncoding
                    && catalog.Codecs.TryGetValue(tcpEncoding, out var codecOptions) && codecOptions.Type == "hl7v2"
                    ? new Hl7v2Codec()
                    : null));
        }

        foreach (var http in endpoints.HttpOutbound)
        {
            registry.RegisterOutboundEndpoint(http.OutputId, output => new HttpOutboundEndpoint(
                new HttpOutboundOptions
                {
                    Endpoint = http.Endpoint,
                    ContentType = http.ContentType,
                    Timeout = TimeSpan.FromSeconds(http.TimeoutSeconds),
                    MaxConnectionsPerServer = http.MaxConnectionsPerServer,
                    PooledConnectionLifetime = TimeSpan.FromSeconds(http.PooledConnectionLifetimeSeconds),
                    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(http.PooledConnectionIdleTimeoutSeconds),
                    Ssl = http.Ssl,
                    Proxy = http.Proxy,
                },
                output.Encoding is { } httpEncoding
                    && catalog.Codecs.TryGetValue(httpEncoding, out var codecOptions) && codecOptions.Type == "hl7v2"
                    ? new Hl7v2Codec()
                    : null));
        }

        foreach (var ws in endpoints.WebSocketOutbound)
        {
            registry.RegisterOutboundEndpoint(ws.OutputId, output => new WebSocketOutboundEndpoint(
                new WebSocketOutboundOptions
                {
                    Endpoint = ws.Endpoint,
                    ExpectReply = ws.ExpectReply,
                    PoolSize = ws.PoolSize,
                    ReceiveBufferSize = ws.ReceiveBufferSize,
                    Ssl = ws.Ssl,
                    Proxy = ws.Proxy,
                },
                output.Encoding is { } wsEncoding
                    && catalog.Codecs.TryGetValue(wsEncoding, out var codecOptions) && codecOptions.Type == "hl7v2"
                    ? new Hl7v2Codec()
                    : null));
        }

        return registry;
    }
}
