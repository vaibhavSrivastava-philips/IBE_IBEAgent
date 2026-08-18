namespace Philips.IBE.IBEAgent.Abstractions;

public static class TransportCorrelationHeaders
{
    public const string RequestId = "transport.requestId";
    public const string MessageId = "transport.messageId";
    public const string LogicalEndpointId = "transport.logicalEndpointId";

    public const string WireRequestId = "X-IBE-Request-Id";
    public const string WireMessageId = "X-IBE-Message-Id";
    public const string WireLogicalEndpointId = "X-IBE-Logical-Endpoint-Id";
}
