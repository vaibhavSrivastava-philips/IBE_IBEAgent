# Contract Reference — `Contracts` array of `contractData.json`

A **contract** is **FSE‑owned**: it wires inputs → outputs and sets operational policy (reply mode,
retry, channels, batching). Copy a starter from this folder into the `"Contracts"` array.

```jsonc
// contractData.json
{ "Endpoints": { ... }, "Contracts": [ /* a contract object */ ] }
```

Wire the endpoints first (see [../endpoints/README.md](../endpoints/README.md)); a contract references
them by **id**: `Inputs[].InputId` → an inbound `SourceEndpointId`, `Outputs[].OutputId` → an outbound
`OutputId`.

**Starters:** [passthrough.jsonc](passthrough.jsonc) (minimal) · [fanout.jsonc](fanout.jsonc) (tuned,
multi‑output) · [request-reply.jsonc](request-reply.jsonc) · [file-relay.jsonc](file-relay.jsonc).

---

## 1. Contract root

| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `Name` | string | **required** | Unique contract name. |
| `Inputs` | list | **required** | One entry per input comm point — see [§2](#2-inputs). (Or the `InputIds` shorthand.) |
| `Outputs` | list | **required** | One entry per output leg — see [§3](#3-outputs). |
| `Workflow` | object | none | `{ "Use": "<name>", "Settings": { … } }` — the developer blueprint (pipeline + encoding + delegated Settings). See [§6](#6-workflow-vs-manual-mode). |
| `Pipeline` | string | none | **Manual mode** only (no `Workflow`): names a catalog `Pipelines` entry. |
| `Acknowledgement` | object | ack on | Reply mode — see [§4](#4-reply-mode). Mutually exclusive with `Response`. |
| `Response` | object | off | Request‑reply mode — see [§4](#4-reply-mode). |
| `ReplyOnFilter` | bool | `false` | On a pipeline‑filtered message: `true` = intentional‑reject ack, `false` = silent drop. (Usually set via a Workflow Setting.) |
| `InputIds` | int[] | none | Shorthand for `Inputs` with default channels (e.g. `[1, 2]`). |

Every contract must resolve each leg's **encoding** somehow: a `Workflow` with a `Format`, a per‑output
`Format`, or an inline `Encoding` — otherwise it fails validation.

---

## 2. Inputs

Each input is one comm point feeding this contract.

| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `InputId` | int | **required** | An inbound endpoint's `SourceEndpointId`. |
| `Channel` | object | defaults | Per‑input queue tuning — see [Channel](#channel). |

```jsonc
"Inputs": [ { "InputId": 1 }, { "InputId": 2, "Channel": { "Capacity": 512, "OverflowPolicy": "Reject" } } ]
```

---

## 3. Outputs

Each output is one **delivery leg**. The shared pipeline runs once, then the message fans out to every
applicable output.

| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `OutputId` | int | **required** | An outbound endpoint's `OutputId`. |
| `Required` | bool | `true` | `true` legs gate the ack; `false` = best‑effort. |
| `DeliveryGuarantee` | enum | `AtMostOnce` | `AtMostOnce` \| `AtLeastOnce` (store‑and‑forward). |
| `FromInputIds` | int[] | all | Restrict this leg to specific inputs; omit = all inputs. |
| `RouteWhen` | object | all | Content filter: `{ "header": "value", … }` matched (AND, exact) against facts a classifier stage set. Omit = all messages. Leave one output **without** `RouteWhen` as a catch‑all. |
| `Format` | string | inherit | Per‑leg encoding override (a catalog `Formats` name). With a multi‑format Workflow it **must** be a declared member. |
| `Encoding` | string | inherit | Inline codec name (a catalog `Codecs` entry) — legacy escape hatch, bypasses `Format`. |
| `Retry` | object | defaults | Per‑leg retry — see [Retry](#retry). |
| `Channel` | object | defaults | Per‑leg queue tuning — see [Channel](#channel). |
| `Batching` | object | off | Batch triggers — see [Batching](#batching). |

```jsonc
"Outputs": [
  { "OutputId": 100 },
  { "OutputId": 200, "Required": false, "FromInputIds": [ 2 ], "Format": "raw-bytes",
    "DeliveryGuarantee": "AtLeastOnce", "Retry": { "MaxAttempts": 5 },
    "Batching": { "Enabled": true, "MaxCount": 500, "MaxLatencyMs": 10000 } }
]
```

---

## 4. Reply mode — `Acknowledgement` **XOR** `Response`

Choose **at most one**. Omit both = default ack.

### `Acknowledgement`
| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `IsEnabled` | bool | `true` | `false` = fire‑and‑forget (no reply bytes). |
| `IsEnhanced` | bool | `false` | `false` = Normal (fires on receipt); `true` = Enhanced (reflects actual delivery). |
| `Shape` | enum | `Single` | `Single`. (`Batch` is reserved and currently **rejected**.) |
| `TimeoutMs` | int | `30000` | Enhanced only: max wait for delivery before NACK; `≤ 0` = no timeout. |

### `Response` (request‑reply)
| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `IsEnabled` | bool | `false` | Return the responder leg's peer reply instead of an ack. |
| `FromOutputId` | int? | sole required | The responder leg whose reply is returned. |
| `TimeoutMs` | int | `30000` | Mandatory wait; on timeout → protocol‑error reply, source released. |

---

## 5. Reusable sub‑objects

### Channel {#channel}
Per‑input and per‑output queue tuning (same shape).
| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `Capacity` | int | `1024` | Bounded queue depth (backpressure). |
| `DegreeOfParallelism` | int | `1` | Concurrent consumers. Cannot combine with `Ordered: true`. |
| `Ordered` | bool | `false` | Preserve order (implies DOP 1). |
| `OverflowPolicy` | enum | `Wait` | `Wait` (async backpressure) \| `Reject` (fail fast) \| `SpillToDisk` (durable — reserved/deferred). |

### Retry {#retry}
Per‑leg inline retry; exhausted retries fall through to store‑and‑forward for `AtLeastOnce`.
| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `MaxAttempts` | int | `3` | Delivery attempts (≥ 1). |
| `BackoffSeconds` | int | `2` | Base backoff. |
| `Backoff` | enum | `Exponential` | `Fixed` \| `Exponential`. |

### Batching {#batching}
FSE owns *whether/when* to batch; the batch **codec** comes from the `Format`.
| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `Enabled` | bool | `false` | Turn batching on for this leg. |
| `MaxCount` | int | `500` | Flush after N messages (≥ 1 when enabled). |
| `MaxLatencyMs` | int | `10000` | Flush after N ms. |
| `Codec` | string | inherit | Inline batch codec override (a catalog `Codecs` entry); omit = the `Format`'s `BatchCodec`. |

---

## 6. Workflow vs. manual mode

**With a Workflow** (recommended) — name it and optionally fill its `Settings`:
```jsonc
"Workflow": { "Use": "adt", "Settings": { "AckTimeoutSeconds": 45 } }
```
The `Settings` a workflow exposes are its friendly, guard‑railed knobs — see
[../workflows/README.md](../workflows/README.md). You can still set raw operational fields
(`Acknowledgement`, `Retry`, `Channel`, …) directly on the contract.

**Manual mode** (no Workflow) — wire the developer concerns inline:
```jsonc
{ "Name": "manual", "Pipeline": "main", "Inputs": [ { "InputId": 1 } ],
  "Acknowledgement": { "TimeoutMs": 45000 },
  "Outputs": [ { "OutputId": 100, "Encoding": "hl7v2" } ] }
```
Operational knobs (ack/response, retry, channel, delivery guarantee, batching, `ReplyOnFilter`) are
**always** settable directly on the contract — a Workflow is not required for them.

---

## 7. Validation (fail‑fast) — the engine rejects a bad contract at startup

- Unique input/output ids; ≥ 1 input and ≥ 1 output; ≥ 1 **required** output when ack is enabled.
- At most one reply mode (`Acknowledgement` XOR `Response`); `Response.FromOutputId` resolves to an output.
- Every `FromInputIds` entry resolves to one of the contract's inputs; every input has ≥ 1 applicable required leg (when ack is on).
- Each leg resolves an `Encoding` (via `Workflow`/`Format`/`Encoding`); if batching is enabled, a batch codec resolves.
- `Capacity` / `DegreeOfParallelism` > 0; `Ordered` not combined with DOP > 1.
- `Workflow.Use` resolves to a catalog workflow; FSE `Settings` obey the workflow's guardrails (see [../workflows/README.md](../workflows/README.md)).

---

## Enums quick reference

| Enum | Values |
|------|--------|
| `DeliveryGuarantee` | `AtMostOnce` · `AtLeastOnce` |
| `OverflowPolicy` | `Wait` · `Reject` · `SpillToDisk` (reserved) |
| `BackoffKind` | `Fixed` · `Exponential` |
| `AckShape` | `Single` (·`Batch` reserved) |
