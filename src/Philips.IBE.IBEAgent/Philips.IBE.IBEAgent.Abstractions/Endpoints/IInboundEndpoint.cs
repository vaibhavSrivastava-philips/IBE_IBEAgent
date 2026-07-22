namespace Philips.IBE.IBEAgent.Abstractions;

public interface IInboundEndpoint          // hosted lifecycle; starts after runtimes, stops first.
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface IOutboundEndpoint         // pooled; serializes via its codec; may send-and-receive.
{
    Task<DeliveryResult> SendAsync(MessageContext context, CancellationToken cancellationToken);
}