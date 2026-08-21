namespace Philips.IBE.IBEAgent.Security;

// Transport-neutral TLS posture, configurable per endpoint (inbound or outbound):
//   Plain   - plaintext, no TLS.
//   OneWay  - TLS with server authentication only (client trusts the server certificate).
//   Mutual  - mutual TLS: both peers authenticate with certificates.
public enum TlsMode
{
    Plain = 0,
    OneWay = 1,
    Mutual = 2
}
