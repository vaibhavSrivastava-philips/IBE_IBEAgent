using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.Endpoints.File;
using Philips.IBE.IBEAgent.Endpoints.Http;
using Philips.IBE.IBEAgent.Endpoints.Tcp;
using Philips.IBE.IBEAgent.Endpoints.WebSocket;
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
        var endpoints = configuration.GetSection("Ibe:Endpoints").Get<OutboundEndpointsOptions>()
            ?? throw new InvalidOperationException("Required configuration section 'Ibe:Endpoints' is missing.");

        // Build the store up front so replay targets below and DI resolve the SAME instance as the ForwardWorker.
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
                    Mode = tcp.Mode,
                    Host = tcp.Host,
                    Port = tcp.Port,
                    PoolSize = tcp.PoolSize,
                    ExpectReply = tcp.ExpectReply,
                    Ssl = tcp.Ssl,
                    Proxy = tcp.Proxy,
                },
                tcp.Encoding == "hl7v2" ? new Hl7v2Codec() : null);
            targets.Add(new KeyValuePair<int, IReplayTarget>(tcp.OutputId, new EndpointReplayTarget(tcp.OutputId, endpoint, store)));
        }

        foreach (var http in endpoints.HttpOutbound)
        {
            var endpoint = new HttpOutboundEndpoint(
                new HttpOutboundOptions
                {
                    Mode = http.Mode,
                    Endpoint = http.Endpoint,
                    ContentType = http.ContentType,
                    Timeout = TimeSpan.FromSeconds(http.TimeoutSeconds),
                    MaxConnectionsPerServer = http.MaxConnectionsPerServer,
                    PooledConnectionLifetime = TimeSpan.FromSeconds(http.PooledConnectionLifetimeSeconds),
                    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(http.PooledConnectionIdleTimeoutSeconds),
                    Ssl = http.Ssl,
                    Proxy = http.Proxy,
                },
                http.Encoding == "hl7v2" ? new Hl7v2Codec() : null);
            targets.Add(new KeyValuePair<int, IReplayTarget>(http.OutputId, new EndpointReplayTarget(http.OutputId, endpoint, store)));
        }

        foreach (var ws in endpoints.WebSocketOutbound)
        {
            var endpoint = new WebSocketOutboundEndpoint(
                new WebSocketOutboundOptions
                {
                    Mode = ws.Mode,
                    Endpoint = ws.Endpoint,
                    ExpectReply = ws.ExpectReply,
                    PoolSize = ws.PoolSize,
                    ReceiveBufferSize = ws.ReceiveBufferSize,
                    Ssl = ws.Ssl,
                    Proxy = ws.Proxy,
                },
                ws.Encoding == "hl7v2" ? new Hl7v2Codec() : null);
            targets.Add(new KeyValuePair<int, IReplayTarget>(ws.OutputId, new EndpointReplayTarget(ws.OutputId, endpoint, store)));
        }

        foreach (var file in endpoints.FileOutbound)
        {
            IMessageCodec? codec = file.Encoding switch
            {
                "hl7v2" => new Hl7v2Codec(),
                "base64" => new Base64Codec(),
                _ => null,
            };
            var endpoint = new FileOutboundEndpoint(
                new FileOutboundOptions
                {
                    Mode = file.Mode,
                    Directory = file.Directory,
                    FileNameTemplate = file.FileNameTemplate,
                    DefaultExtension = file.DefaultExtension,
                },
                codec);
            targets.Add(new KeyValuePair<int, IReplayTarget>(file.OutputId, new EndpointReplayTarget(file.OutputId, endpoint, store)));
        }

        services.AddForwardWorker(configuration, targets);

        return services;
    }
}
