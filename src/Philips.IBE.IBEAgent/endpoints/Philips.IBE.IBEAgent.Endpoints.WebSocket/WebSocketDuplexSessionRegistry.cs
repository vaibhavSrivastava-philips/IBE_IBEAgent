using System.Collections.Concurrent;

namespace Philips.IBE.IBEAgent.Endpoints.WebSocket;

public sealed class WebSocketDuplexSessionRegistry
{
    private readonly ConcurrentDictionary<int, WebSocketDuplexSession> _sessions = new();

    internal void Register(WebSocketDuplexSession session)
    {
        if (_sessions.TryGetValue(session.SourceEndpointId, out var previous))
            previous.Dispose();
        _sessions[session.SourceEndpointId] = session;
    }

    internal void Unregister(WebSocketDuplexSession session)
    {
        _sessions.TryRemove(new KeyValuePair<int, WebSocketDuplexSession>(session.SourceEndpointId, session));
        session.Dispose();
    }

    internal bool TryGet(int sourceEndpointId, out WebSocketDuplexSession? session)
        => _sessions.TryGetValue(sourceEndpointId, out session);
}
