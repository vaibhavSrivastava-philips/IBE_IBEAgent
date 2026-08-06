namespace Philips.IBE.IBEAgent.Security;

// Transport-neutral SSL/TLS posture, configurable per endpoint (inbound or outbound):
//   None    - plaintext, no TLS.
//   OneWay  - TLS with server authentication only (client trusts the server certificate).
//   TwoWay  - mutual TLS: server also authenticates the client (or vice-versa for an outbound leg).
public enum SslMode
{
    None,
    OneWay,
    TwoWay
}
