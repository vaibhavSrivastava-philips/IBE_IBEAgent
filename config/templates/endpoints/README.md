# Endpoint (Comm Point) Reference — `Endpoints` section of `contractData.json`

Endpoints are **FSE topology**: the physical inbound/outbound comm points. Copy a starter from this
folder into the `"Endpoints"` object of `contractData.json`, then tune the values.

```jsonc
// contractData.json
{
  "Endpoints": {
    "TcpInbound":  [ /* tcp-inbound.jsonc  */ ],
    "TcpOutbound": [ /* tcp-outbound.jsonc */ ],
    "HttpInbound": [ ... ], "HttpOutbound": [ ... ],
    "WebSocketInbound": [ ... ], "WebSocketOutbound": [ ... ],
    "FileInbound": [ ... ], "FileOutbound": [ ... ]
  },
  "Contracts": [ ... ]
}
```

**Two golden rules**
- Every **inbound** endpoint needs a unique **`SourceEndpointId`** — a contract's `Inputs[].InputId` points at it.
- Every **outbound** endpoint needs a unique **`OutputId`** — a contract's `Outputs[].OutputId` points at it.

Times are written as **whole seconds** on outbound endpoints (`...Seconds`) and as a `TimeSpan`
string (`"00:00:30"`) where noted. `Format` is the *input* parser/ack tag (`hl7v2` today).

---

## TCP

### `TcpInbound` (MLLP listener) — [tcp-inbound.jsonc](tcp-inbound.jsonc)
| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `SourceEndpointId` | int | **required** | Unique id referenced by `Inputs[].InputId`. |
| `Port` | int | **required** | MLLP TCP listen port. |
| `BindAddress` | string | `"0.0.0.0"` | Interface to bind (`0.0.0.0` = all, `127.0.0.1` = loopback). |
| `Format` | string | `"hl7v2"` | Message format (parser/stages/ack formatter). |
| `MaxConcurrentMessages` | int | `100` | Admission control (bounded). |
| `Ssl` | object | none | TLS — see [SSL](#ssl-shared). |

### `TcpOutbound` (pooled MLLP sender) — [tcp-outbound.jsonc](tcp-outbound.jsonc)
| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `OutputId` | int | **required** | Unique id referenced by `Outputs[].OutputId`. |
| `Host` | string | **required** | Destination host. |
| `Port` | int | **required** | Destination MLLP port. |
| `PoolSize` | int | `8` | Pooled persistent connections. |
| `ExpectReply` | bool | `true` | Read the MLLP ack frame back (feeds enhanced ack / request‑reply). |
| `Ssl` / `Proxy` | object | none | TLS / forward proxy — see below. |
| *Duplex (advanced):* `SourceEndpointId`, `DuplexInboundSourceEndpointId`, `InboundFormat` (`"hl7v2"`), `ReplyCorrelationTimeoutSeconds` (`30`), `ReconnectDelaySeconds` (`2`) | | | For a duplex outbound that also receives on a paired inbound. |

---

## HTTP

### `HttpInbound` — [http-inbound.jsonc](http-inbound.jsonc)
| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `SourceEndpointId` | int | **required** | Unique id referenced by `Inputs[].InputId`. |
| `Prefix` | string | **required** | `HttpListener` prefix, trailing slash required (`"http://localhost:8080/ibe/"`; `https://` with `Ssl`). |
| `Format` | string | `"hl7v2"` | Message format. |
| `MaxConcurrentRequests` | int | `200` | Admission control. |
| `ReplyTimeout` | TimeSpan | `"00:00:30"` | How long the request is held open for a reply. |
| `RelayContentType` | bool | `false` | Capture the request `Content-Type` so an HTTP output can relay it. |
| `Ssl` | object | none | Client‑cert enforcement (`TwoWay` = mTLS); the port's server cert is bound out‑of‑process. |

### `HttpOutbound` — [http-outbound.jsonc](http-outbound.jsonc)
| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `OutputId` | int | **required** | Unique id referenced by `Outputs[].OutputId`. |
| `Endpoint` | URI | **required** | Destination URL. |
| `ContentType` | string | `"application/octet-stream"` | Request content type (unless relayed). |
| `TimeoutSeconds` | int | `30` | Request timeout. |
| `MaxConnectionsPerServer` | int | `8` | Pooled connections per host (~ TCP `PoolSize`). |
| `PooledConnectionLifetimeSeconds` | int | `300` | Recycle a pooled connection after N s (picks up DNS changes). |
| `PooledConnectionIdleTimeoutSeconds` | int | `120` | Close an idle pooled connection after N s. |
| `Ssl` / `Proxy` | object | none | TLS / forward proxy. |

---

## WebSocket

### `WebSocketInbound` — [ws-inbound.jsonc](ws-inbound.jsonc)
| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `SourceEndpointId` | int | **required** | Unique id referenced by `Inputs[].InputId`. |
| `Prefix` | string | **required** | Listener prefix (`"http://localhost:8080/ibe/ws/"`; `https://` for `wss`). |
| `Format` | string | `"hl7v2"` | Message format. |
| `MaxConcurrentMessages` | int | `100` | Admission control. |
| `ReceiveBufferSize` | int | `8192` | Per‑connection receive buffer (bytes). |
| `Ssl` | object | none | Client‑cert enforcement (`TwoWay` = mTLS). |

### `WebSocketOutbound` — [ws-outbound.jsonc](ws-outbound.jsonc)
| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `OutputId` | int | **required** | Unique id referenced by `Outputs[].OutputId`. |
| `Endpoint` | URI | **required** | Destination `ws://` or `wss://` URL. |
| `PoolSize` | int | `8` | Pooled persistent connections. |
| `ReceiveBufferSize` | int | `8192` | Per‑connection receive buffer. |
| `ExpectReply` | bool | `true` | Read one reply frame back. |
| `Ssl` / `Proxy` | object | none | TLS / forward proxy. |
| *Duplex (advanced):* `SourceEndpointId`, `DuplexInboundSourceEndpointId`, `InboundFormat`, `ReplyCorrelationTimeoutSeconds` (`30`), `ReconnectDelaySeconds` (`2`) | | | Duplex outbound + paired inbound. |

---

## File

### `FileInbound` (folder poller) — [file-inbound.jsonc](file-inbound.jsonc)
| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `SourceEndpointId` | int | **required** | Unique id referenced by `Inputs[].InputId`. |
| `Directory` | string | **required** | Folder to poll (local path, or a UNC share). |
| `FilePattern` | string | all | `";"`‑delimited extension globs (`"*.hl7;*.txt"`); blank/omit = every file. |
| `Recursive` | bool | `false` | Poll subfolders too. |
| `PollIntervalSeconds` | int | `10` | Seconds between directory scans. |
| `Format` | string | `"hl7v2"` | Message format. |
| `KeepOriginalFiles` | bool | `false` | `false` → move consumed files to `processed/`/`error/`; `true` → leave in place and advance a hidden marker (read‑only shares). |
| `RetentionDays` | int | `0` | Delete disposed files older than N days; `0` = keep forever. |
| `Username` / `Domain` / `PasswordProtected` | string | none | UNC‑share auth (Windows); `PasswordProtected` is a DPAPI‑protected base64 password — **never** a plaintext password. |

### `FileOutbound` (atomic writer) — [file-outbound.jsonc](file-outbound.jsonc)
| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `OutputId` | int | **required** | Unique id referenced by `Outputs[].OutputId`. |
| `Directory` | string | **required** | Destination folder (created if missing). |
| `FileNameTemplate` | string | default | Tokens: `{timestamp}` (UTC), `{correlationId}`, `{messageId}`, `{ext}`. Omit = `Message_{timestamp}_{correlationId}.{ext}`. |
| `DefaultExtension` | string | `"txt"` | Fills the `{ext}` token. |
| `AllowMessageDirectedPath` | bool | `true` | Honor a blob envelope's `destinationpath` as the output dir; `false` = always write to `Directory`. |

> The output leg's **encoding** is **not** set here — it comes from the contract (the Workflow's `Format`,
> a per‑output `Format`, or an inline `Encoding`), same as TCP/HTTP.

---

## SSL (shared) {#ssl-shared}

Available on every TCP/HTTP/WebSocket endpoint via `"Ssl": { … }`. TLS turns on when certificate
material is configured (or `Mode`/`Enabled` is set). Common keys:

| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `Mode` | enum | `None` | `None` \| `OneWay` (validate the peer) \| `TwoWay` (mutual TLS). |
| `Enabled` | bool? | inferred | Preferred switch; if omitted, inferred from `Mode`/cert material. |
| `LocalCertificate` | object | none | This side's certificate (server for inbound; client for `TwoWay` outbound). Use `Kind=WindowsStore` for production. |
| `TrustedCertificateAuthority` | object | none | Pinned CA to validate the remote peer (private/self‑signed PKI). Use `Kind=WindowsStore` for production. |
| `RequireClientCertificate` | bool | `false` | Inbound: require + validate a client cert (mTLS). |
| `AllowUntrustedCertificate` | bool | `false` | **Dev/test only** — accept any peer cert. Never in production. |
| `CheckCertificateRevocation` | bool | `false` | Enable CRL/OCSP checks. |

**`LocalCertificate` / `TrustedCertificateAuthority` sub-keys (`CertificateReference`):**

| Key | Type | Meaning |
|-----|------|---------|
| `Kind` | enum | `WindowsStore` (production) · `File` (dev/test only) · `LinuxStore` · `MountedSecret` |
| `StoreName` | string | Windows store name, e.g. `My` (default), `Root`, `CA`. |
| `StoreLocation` | string | `LocalMachine` (default for services) or `CurrentUser`. |
| `Subject` | string | Certificate CN / subject — renewal‑safe; selects the newest valid match. |
| `FriendlyName` | string | Alternative to `Subject` for Windows store lookup. |
| `Thumbprint` | string | Exact thumbprint match (pinned, renewal‑unsafe — avoid in production). |
| `Path` | string | **File only** — path to `.pfx` or `.pem` file. Dev/test only. |
| `Password` | string | **File only** — PFX password. Dev/test only. |

**Production example (`WindowsStore` by Subject):**
```json
"Ssl": {
  "Mode": "OneWay",
  "LocalCertificate": {
    "Kind": "WindowsStore",
    "StoreName": "My",
    "StoreLocation": "LocalMachine",
    "Subject": "CN=my-service.example.com"
  }
}
```

## Proxy (shared, outbound only)

`"Proxy": { … }` on TCP/HTTP/WebSocket **outbound** endpoints — an HTTP CONNECT forward tunnel.

| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `IsEnabled` | bool | `false` | Route through the proxy. |
| `Host` | string | none | Proxy host (no port here). |
| `Port` | int | `0` | Proxy port. |
| `Username` / `Password` | string | none | Optional basic‑auth creds; omit both for an anonymous proxy. |

---

## Enums

- **`SslMode`**: `None` · `OneWay` · `TwoWay`
- **`CommunicationMode`** (`Mode`): `Inbound` · `Outbound` · `DuplexInbound` · `DuplexOutbound` (rarely set by hand; the endpoint kind implies it)

Once your endpoints are wired, author the contracts that connect them → see
[../contracts/README.md](../contracts/README.md).
