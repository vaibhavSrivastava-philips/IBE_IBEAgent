namespace Philips.IBE.IBEAgent.Abstractions;

// Direction/session role only. ACK/no-ACK/request-reply behavior is configured separately by
// acknowledgement/response policy; do not encode it in these names.
public enum CommunicationMode
{
    Inbound,
    Outbound,
    DuplexInbound,
    DuplexOutbound,
}
