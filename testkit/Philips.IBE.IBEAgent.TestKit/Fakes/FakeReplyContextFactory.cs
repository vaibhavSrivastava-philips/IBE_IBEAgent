using Philips.IBE.IBEAgent.Abstractions;
namespace Philips.IBE.IBEAgent.TestKit;

public sealed class FakeReplyContextFactory : IReplyContextFactory
{
    public List<RecordingReplyContext> Created { get; } = [];
    public IReplyContext Create(int sourceEndpointId, IAckToken ackToken)
    {
        var rc = new RecordingReplyContext();
        Created.Add(rc);
        return rc;
    }
}