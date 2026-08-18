# Configuration Examples: Protocol Modes and Runtime Options

These examples use direction/session-only `Mode` values. ACK/no-ACK/request-reply behavior remains separate in contract acknowledgement/response policy and transport-specific `ExpectReply` settings.

## TCP / MLLP

### Half-duplex inbound and outbound

```json
{
  "Endpoints": {
    "TcpInbound": [
      { "Mode": "Inbound", "SourceEndpointId": 1, "Port": 6000, "Format": "hl7v2" }
    ],
    "TcpOutbound": [
      { "Mode": "Outbound", "OutputId": 100, "Host": "partner", "Port": 7000, "ExpectReply": true }
    ]
  }
}
```

### Full-duplex, agent-initiated session

```json
{
  "Endpoints": {
    "TcpOutbound": [
      {
        "Mode": "DuplexOutbound",
        "OutputId": 100,
        "SourceEndpointId": 1,
        "InboundFormat": "hl7v2",
        "Host": "partner",
        "Port": 7000,
        "ExpectReply": true,
        "ReplyCorrelationTimeoutSeconds": 30,
        "ReconnectDelaySeconds": 2
      }
    ]
  }
}
```

### Full-duplex, partner-initiated accepted session

```json
{
  "Endpoints": {
    "TcpInbound": [
      { "Mode": "DuplexInbound", "SourceEndpointId": 1, "Port": 6000, "Format": "hl7v2" }
    ],
    "TcpOutbound": [
      {
        "Mode": "DuplexInbound",
        "OutputId": 100,
        "DuplexInboundSourceEndpointId": 1,
        "Host": "unused-for-accepted-session",
        "Port": 1,
        "ExpectReply": true
      }
    ]
  }
}
```

## HTTP / HTTPS logical bidirectional pair

HTTP bidirectional behavior is modeled as paired logical endpoints, not a socket-level full-duplex session.

```json
{
  "Endpoints": {
    "HttpInbound": [
      {
        "Mode": "DuplexInbound",
        "LogicalEndpointId": "partner-a",
        "SourceEndpointId": 10,
        "Prefix": "http://localhost:8080/ibe/callback/",
        "Format": "hl7v2"
      }
    ],
    "HttpOutbound": [
      {
        "Mode": "DuplexOutbound",
        "LogicalEndpointId": "partner-a",
        "OutputId": 110,
        "Endpoint": "https://partner.example/ibe/inbound",
        "ContentType": "application/hl7-v2",
        "TimeoutSeconds": 30
      }
    ]
  }
}
```

## WebSocket full duplex

```json
{
  "Endpoints": {
    "WebSocketOutbound": [
      {
        "Mode": "DuplexOutbound",
        "OutputId": 120,
        "SourceEndpointId": 20,
        "InboundFormat": "hl7v2",
        "Endpoint": "wss://partner.example/ibe/ws",
        "ExpectReply": true
      }
    ]
  }
}
```

For partner-initiated WebSocket sessions, configure `WebSocketInbound` as `DuplexInbound` and an outbound leg with `DuplexInboundSourceEndpointId`.

## Generic transport envelope

TCP/MLLP and WebSocket can optionally carry correlation metadata by sending a JSON envelope instead of a raw payload. Raw payload behavior remains supported; malformed or non-envelope payloads are treated as raw bytes.

```json
{
  "correlationId": "optional-correlation-id",
  "requestId": "optional-request-id",
  "messageId": "optional-message-id",
  "logicalEndpointId": "optional-logical-endpoint-id",
  "payload": "text payload"
}
```

For binary payloads, use `payloadBase64` instead of `payload`:

```json
{
  "correlationId": "optional-correlation-id",
  "payloadBase64": "BASE64_BYTES"
}
```

The Agent stores generic metadata in `MessageContext.Headers` using `transport.requestId`, `transport.messageId`, and `transport.logicalEndpointId`. HTTP uses the same internal metadata and maps it to `X-IBE-Request-Id`, `X-IBE-Message-Id`, and `X-IBE-Logical-Endpoint-Id` wire headers.

## File logical bidirectional pair

File transport is directory-based. Bidirectional behavior is a logical pair of inbound and outbound locations.

```json
{
  "Endpoints": {
    "FileInbound": [
      {
        "Mode": "DuplexInbound",
        "LogicalEndpointId": "folder-pair-a",
        "SourceEndpointId": 30,
        "Directory": "D:\\IBE\\in",
        "FilePattern": "*.hl7"
      }
    ],
    "FileOutbound": [
      {
        "Mode": "DuplexOutbound",
        "LogicalEndpointId": "folder-pair-a",
        "OutputId": 130,
        "Directory": "D:\\IBE\\out",
        "FileNameTemplate": "{correlationId}.hl7"
      }
    ]
  }
}
```

## TLS and mTLS inference

TLS is inferred from secure listener/endpoint settings and certificate material. Mutual TLS is inferred from local certificate material on outbound connections or `RequireClientCertificate` plus trust settings on inbound listeners. ACK/request-reply behavior is not encoded in TLS mode names.

## Opt-in runtime reload

Default behavior remains startup compilation. To enable atomic runtime replacement on configuration reload:

```json
{
  "EngineReload": {
    "Enabled": true,
    "DebounceMilliseconds": 500
  }
}
```

Invalid reloads are rejected and the previous engine snapshot remains active.

## Configuration schema migration

Current schema documents default to:

```json
{ "SchemaVersion": 1 }
```

The migration normalizer accepts schema version `1`, rejects future/invalid versions, and normalizes legacy `InputIds` shorthand into explicit `Inputs` entries.
