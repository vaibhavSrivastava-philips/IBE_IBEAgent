using Philips.IBE.IBEAgent.Configuration;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.Endpoints.Http;
using Philips.IBE.IBEAgent.Endpoints.Tcp;
using Philips.IBE.IBEAgent.Formats.Hl7;

namespace Philips.IBE.IBEAgent.Service;

// §3.10 — composition-time registration: builds the ComponentRegistry with the concrete stages,
// codecs, and outbound endpoint factories this deployment supports. New protocols/codecs are
// added here (register + name), never by editing Core.
public static class ComponentRegistryBuilder
{
    public static ComponentRegistry Build(AgentEndpointsOptions endpoints, CatalogOptions catalog)
    {
        var registry = new ComponentRegistry();

        // §3.8/§6 — Enhanced-ack rendering: HL7's own (Format x Shape) generated ack.
        registry.RegisterAckFormatter(new Hl7SingleAckFormatter());

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
                },
                output.Encoding is { } httpEncoding
                    && catalog.Codecs.TryGetValue(httpEncoding, out var codecOptions) && codecOptions.Type == "hl7v2"
                    ? new Hl7v2Codec()
                    : null));
        }

        return registry;
    }
}
