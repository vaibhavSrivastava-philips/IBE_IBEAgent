# IBEAgent — Vaibhav's TODO & Progress

> Working tracker for Vaibhav's stream of the greenfield IBEAgent build.
> Source of truth for the design: [architecture/Refactor_ArchitectureDoc_v4.md](architecture/Refactor_ArchitectureDoc_v4.md).
> Roadmap phases referenced below come from that doc's §14.

_Last updated: 2026-07-27_

---

## ✅ What has been achieved

**Phase 1 — Abstractions & envelope (complete)**
- `MessageContext` (envelope, `CloneForLeg`, `ReplacePayload`, `MarkReplay`), `DeliveryResult`, `PipelineResult`, enums (`DeliveryOutcome`, `DeliveryGuarantee`, `AckShape`, `OverflowPolicy`, `BackoffKind`, `ForwardStatus`).
- All cross-layer interfaces in `Abstractions`: `IAckToken`, `IReplyContext` (+ `Attach`), `IReplyContextFactory`, `IMessageDispatcher`, `IRouteResolver`, `IContractRuntime`, `IMessageChannel`, `IMessagePipeline`/`IMessageStage`, `IInbound/OutboundEndpoint`, `IMessageCodec`/`IBatchCodec`, `IAckStrategy`, `IAckFormatter`, `IForwardStore` (+ `ForwardEntry`).
- TestKit doubles: `FakeAckToken`, `RecordingReplyContext`, `FakeInboundEndpoint`, `FakeOutboundEndpoint`, `FakeMessageCodec`, `FakeMessageDispatcher`, `FakeReplyContextFactory`, `MessageContextBuilder`.

**Architecture doc consistency pass (A1–A5)** — reconciled §14 (greenfield), `IReplyContext` seam, interface-placement rule, `ReplyContext` method names, header lifecycle. Plus added §4.2 (clone/memory lifetime) and the §6 reply decision-flow diagram.

**Endpoints (Phase 5 pulled forward — transport layer only)**
- TCP/MLLP: `MllpFramer`, `TcpConnectionAckToken`, `TcpConnectionPool` (pooled), `TcpInboundEndpoint` (listener), `TcpOutboundEndpoint` (client + optional reply capture).
- HTTP: `HttpResponseAckToken` (one-shot, TCS-gated), `HttpInboundEndpoint` (`HttpListener`, held-connection + reply timeout → 504), `HttpOutboundEndpoint` (shared `HttpClient` + tuned `SocketsHttpHandler` pooling).
- Codec is **optional** (null → raw pass-through) on both outbound endpoints.
- Integration tests: `MllpFramer` round-trip + edges, TCP in/out, HTTP in/out (loopback).

**Core spine — runtime/queue half + reply trio (Phase 3 partial)**
- `BoundedInMemoryChannel` (Wait/Reject; SpillToDisk rejected → Phase 6), `QueueFullException`.
- `ContractRuntime` (per-input ingress queues, shared pipeline once, fan-out, `FromInputIds` filter, per-message required count, drain).
- `DeliveryLeg` (own queue, deliver via endpoint, report to reply, leg-targeted `ReplayAsync`, `IForwardStore?` optional).
- `PassThroughPipeline`, `ReplyContext` (one-shot + timeout), `NormalAckStrategy` (**stubbed ACK**), `ReplyContextFactory`.
- 26 Core unit tests, all green; ~100% behavioral coverage (only deferred/defensive branches uncovered).

---

## 🔜 Immediate TODOs (next up)

- [ ] **Port the legacy HL7 ACK generator** → `Hl7SingleAckFormatter : IAckFormatter` in `Formats.Hl7` (keyed `(hl7v2, Single)`), and change `NormalAckStrategy` to delegate via `IAckFormatter` instead of the hardcoded `MSA|AA|received`. Keeps Core HL7-free (P2/§3.8). _(Phase 5; can be pulled forward once the legacy function is shared.)_
- [ ] **Close the `DeliveryLeg` coverage gap** — add a fake `IForwardStore` test asserting `StoreAsync` on a failed delivery and `ResolveAsync` on a delivered replay (pre-validates the Phase 6 seam, pulls `DeliveryLeg` to ~100%).
- [ ] **Verify inbound endpoints call `ctx.Reply.Attach(ctx)`** before `DispatchAsync` (required so `ReplyContext` can write via the message token).
- [ ] **End-to-end slice integration test** (once the routing trio lands): swap `FakeMessageDispatcher` for the real `Dispatcher`, hand-wire one contract (no `ContractCompiler` yet), assert TCP→TCP + ACK back to source.
- [ ] _(optional)_ decide whether to keep `QueueFullException`'s unused standard ctors (CA1032 boilerplate) or trim them.

## 🚧 Coordination (parallel work — other developer)

- **Routing trio:** `Dispatcher` (`IMessageDispatcher`), `SourceBasedRouter` (`IRouteResolver`), `ContractRegistry`.
- **Integration seam:** `IContractRuntime`. Agreed behavioral contracts: dispatch → `Resolve` → `runtime.EnqueueAsync`; runtime routes to per-input queue by `SourceEndpointId`; host calls `RunAsync` once; `EnqueueAsync` backpressures when full.
- **Simplification agreed:** `ContractRegistry` may be a thin dictionary wrapper (or folded into `SourceBasedRouter`); `ContractCompiler` is deferred (hand-wire for the first slice).

---

## 📋 Phase roadmap (status)

| Phase | Scope | Status |
|---|---|---|
| **1. Abstractions & envelope** | neutral types, enums, all interfaces, TestKit | ✅ Done |
| **2. Configuration** | option + Catalog DTOs, `IValidateOptions` validators, JSON schema | ⛔ Not started |
| **3. Core spine — single leg (in-memory)** | Dispatcher/Router/Registry, ContractRuntime, DeliveryLeg, BoundedInMemoryChannel, ReplyContext, minimal ContractCompiler | 🟡 In progress — runtime/queue + reply trio done; **routing trio + end-to-end wiring pending** |
| **4. Multi-output fan-out + reply/ack matrix** | Outputs, per-leg queues, concurrent fan-out, FromInputIds, required/optional, Normal/Enhanced ack, one-shot + timeout | 🟡 Partial — fan-out/filter/required-count/one-shot/timeout done in Core; **`EnhancedAckStrategy`, multi-leg `ContractCompiler`, fan-out validation pending** |
| **5. Formats, endpoints, codecs, stages** | Formats.Hl7 (parser/codec/`Hl7SingleAckFormatter`/filter/MSH-10/dedup), Endpoints File + CimS3, `ParallelStage` | 🟡 Partial — TCP + HTTP endpoints done; **Formats.Hl7, File, CimS3, real codecs/formatter/stages pending** |
| **6. Durability, store-and-forward, security** | DurableChannel, IForwardStore impl, ForwardWorker (backoff/cap/park), replay, DPAPI, AvroZip | ⛔ Not started (seams in place: `IMessageChannel`, `IForwardStore`, `DeliveryLeg._forward`, `ReplayAsync`) |
| **7. Hosts, telemetry, request-reply, Web, cutover** | Service + ForwardService hosts, OTel, `ResponseReplyStrategy` + send-receive, WebAgent, batch ack | ⛔ Not started (host projects are stubs) |

**Critical path:** 1 → 3 → 4. Vaibhav owns the runtime/queue + endpoints spine; routing is parallel; formats/durability/hosts hang off the frozen interfaces (no engine edits).
