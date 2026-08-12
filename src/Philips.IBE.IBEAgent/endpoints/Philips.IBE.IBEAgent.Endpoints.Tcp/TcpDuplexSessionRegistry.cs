using System.Collections.Concurrent;

namespace Philips.IBE.IBEAgent.Endpoints.Tcp;

public sealed class TcpDuplexSessionRegistry
{
    private readonly ConcurrentDictionary<int, TcpDuplexSession> _sessions = new();

    internal void Register(TcpDuplexSession session)
    {
        if (_sessions.TryGetValue(session.SourceEndpointId, out var previous))
            previous.Dispose();
        _sessions[session.SourceEndpointId] = session;
    }

    internal void Unregister(TcpDuplexSession session)
    {
        _sessions.TryRemove(new KeyValuePair<int, TcpDuplexSession>(session.SourceEndpointId, session));
        session.Dispose();
    }

    internal bool TryGet(int sourceEndpointId, out TcpDuplexSession? session)
        => _sessions.TryGetValue(sourceEndpointId, out session);
}
