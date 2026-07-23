using Philips.IBE.IBEAgent.Abstractions;
namespace Philips.IBE.IBEAgent.TestKit;

public sealed class FakeMessageDispatcher : IMessageDispatcher
{
    private readonly List<MessageContext> _dispatched = [];
    public IReadOnlyList<MessageContext> Dispatched => _dispatched;
    public Task DispatchAsync(MessageContext context, CancellationToken cancellationToken)
    {
        _dispatched.Add(context);
        return Task.CompletedTask;
    }
}