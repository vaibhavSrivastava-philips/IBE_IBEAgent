namespace Philips.IBE.IBEAgent.Abstractions;

public interface IRequiresInboundDispatch
{
    void ConfigureInboundDispatch(IMessageDispatcher dispatcher, IReplyContextFactory replyFactory);
}
