using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.TestKit;

public sealed class FakeOutboundEndpoint(Func<MessageContext, DeliveryResult>? behavior = null) : IOutboundEndpoint
{
    private readonly Func<MessageContext, DeliveryResult> _behavior =
        behavior ?? (_ => new DeliveryResult(DeliveryOutcome.Delivered));
    private readonly List<MessageContext> _sent = [];
    public IReadOnlyList<MessageContext> Sent => _sent;

    public Task<DeliveryResult> SendAsync(MessageContext context, CancellationToken cancellationToken)
    {
        _sent.Add(context);
        return Task.FromResult(_behavior(context));
    }
}