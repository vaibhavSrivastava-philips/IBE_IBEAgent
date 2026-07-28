using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Endpoints.Http;
using Philips.IBE.IBEAgent.Endpoints.Tcp;
using Philips.IBE.IBEAgent.Formats.Hl7;
using Philips.IBE.IBEAgent.Persistence;

namespace Philips.IBE.IBEAgent.ForwardService;

// §3.9/§3.10 — the out-of-process composition root: builds the SAME kind of outbound endpoints
// (by OutputId) the in-process engine uses, wraps each as an EndpointReplayTarget against the
// shared IForwardStore, and registers AddForwardWorker. Never references Core/Service (this host
// only replays already-failed deliveries; it does not compile contracts or run inbound endpoints).
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddForwardService(this IServiceCollection services, IConfiguration configuration)
    {
        var endpoints = configuration.GetSection("Ibe:Endpoints").Get<OutboundEndpointsOptions>() ?? new OutboundEndpointsOptions();

        // AddForwardStore registers IForwardStore via DI type registration; build one instance up
        // front instead (mirrors Philips.IBE.IBEAgent.Service's own composition-time wiring) so the
        // replay targets below and DI resolve the SAME store instance.
        var protector = Security.DataProtectorFactory.Create();
        var store = new InMemoryForwardStore(protector);
        services.AddSingleton(protector);
        services.AddSingleton<IForwardStore>(store);

        var targets = new List<KeyValuePair<int, IReplayTarget>>();

        foreach (var tcp in endpoints.TcpOutbound)
        {
            var endpoint = new TcpOutboundEndpoint(
                new TcpOutboundOptions
                {
                    Host = tcp.Host,
                    Port = tcp.Port,
                    PoolSize = tcp.PoolSize,
                    ExpectReply = tcp.ExpectReply,
                },
                tcp.Encoding == "hl7v2" ? new Hl7v2Codec() : null);
            targets.Add(new KeyValuePair<int, IReplayTarget>(tcp.OutputId, new EndpointReplayTarget(tcp.OutputId, endpoint, store)));
        }

        foreach (var http in endpoints.HttpOutbound)
        {
            var endpoint = new HttpOutboundEndpoint(
                new HttpOutboundOptions
                {
                    Endpoint = http.Endpoint,
                    ContentType = http.ContentType,
                    Timeout = TimeSpan.FromSeconds(http.TimeoutSeconds),
                },
                http.Encoding == "hl7v2" ? new Hl7v2Codec() : null);
            targets.Add(new KeyValuePair<int, IReplayTarget>(http.OutputId, new EndpointReplayTarget(http.OutputId, endpoint, store)));
        }

        services.AddForwardWorker(configuration, targets);

        return services;
    }
}
