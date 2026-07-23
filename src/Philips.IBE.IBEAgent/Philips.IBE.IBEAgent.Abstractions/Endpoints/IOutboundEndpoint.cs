namespace Philips.IBE.IBEAgent.Abstractions;

public interface IOutboundEndpoint         // pooled; serializes via its codec; may send-and-receive.
{
    Task<DeliveryResult> SendAsync(MessageContext context, CancellationToken cancellationToken);
}