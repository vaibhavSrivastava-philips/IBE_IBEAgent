# Vaibhav — IBE Agent TODO / deferred items

Running list of design items intentionally deferred, with enough detail to pick up later.

## Durable / persistent forward store (crash-safe AtLeastOnce)

**Status:** NOT implemented. `IForwardStore` has only `InMemoryForwardStore` (encrypts at rest via
`IDataProtector`, but lives in process memory). `AtLeastOnce` legs retry with backoff and a park cap,
but the buffer is **lost on an agent crash** — the arch-doc "never lost" outbox (§7/§3.9) and the
`DurableChannel` (persist-on-enqueue) are not built.

**Why it matters:** this is the replacement for the legacy File "leave the file on disk and re-poll"
retry. Legacy's on-disk file survives a crash; our in-memory forward store does not — so today
`AtLeastOnce` gives bounded retry but **not** crash-durable retry. Closing this makes `AtLeastOnce`
fully supersede the legacy File failure behavior (leg-targeted, bounded, backoff, park — and durable).

**What it must do (§3.9/§7):**
- A persistent `IForwardStore` (e.g. SQLite/Postgres) holding the **post-pipeline canonical payload +
  header snapshot + OutputId** (INV-5), encrypted at rest — never raw inbound bytes.
- Crash-safe outbox ordering: deliver → confirm → then `ResolveAsync` (delete). A crash between send
  and delete re-delivers on restart (at-least-once); dedup/idempotency absorbs the rare duplicate.
- `Pending | Parked` lifecycle surviving restart; `Requeue`/`Discard` ops + retention/purge for parked
  rows (surfaced by the Web read-model).
- Optionally a `DurableChannel` (persist-on-enqueue) for the leg queue itself, so a message is durable
  the moment it is accepted — not only after a delivery failure.
- Both hosts (in-proc `ForwardWorker` + out-of-proc `ForwardService`) share the same store + delivery
  path (no duplicate senders).

## Batch acknowledgement (`AckShape.Batch`) — HL7 `BHS..BTS`

**Status:** NOT implemented. `Hl7BatchAckFormatter` exists as a documented stub; contracts that set
`Acknowledgement.Shape = Batch` are rejected at config validation (`ContractOptionsValidator`) until
this lands. The `Single` shape (relay one leg's ack / one generated NACK on failure) is implemented.

**What it must do (§6; HL7 v2 batch protocol):**
Render ONE batch acknowledgement wrapping N units — one `MSH`/`MSA`[/`ERR`] per unit — inside a
`BHS` (batch header) … `BTS` (batch trailer, count = N) envelope. A "unit" is one settled item:
- **multi-output enhanced ack** → one unit per **required output leg** (fan-out case), or
- **inbound `BHS..BTS` batch** → one unit per **inbound message** (de-batched at the input).

Both feed the **same** renderer; only the source of the N results differs.

**Per-unit rule:**
- delivered **and** returned ack bytes → embed that downstream ack **verbatim** (relay).
- delivered but returned no bytes → generate a positive `MSA` (`AA`) via `HL7AckGenerator`.
- **failed**, or **unarrived at timeout** → generate a negative `MSA` (`AE`) NACK
  (`HL7AckGenerator.GenerateHL7Reject` / `BuildFallbackNack`), so the batch is always complete.
- Units ordered deterministically by `OutputId`.

**Edge cases:** single unit (degenerate batch of one, `BTS|1`); all failed (batch of NACKs);
unparseable downstream ack → fall back to a generated `AE` unit (the reply path never throws);
`MSH` echo / control-id per unit follows `Hl7SingleAckFormatter`.

**Wiring notes (when built):**
- `IAckFormatter.Render` is single-result; a batch needs the whole set → add a batch-capable seam
  (`IBatchAckFormatter.Render(MessageContext, IReadOnlyList<DeliveryResult>)`) that
  `Hl7BatchAckFormatter` implements. Register under `(hl7v2, AckShape.Batch)` in
  `ComponentRegistryBuilder`.
- `EnhancedAckStrategy`'s `Delivered` branch dispatches to the batch renderer when `Shape == Batch`.
- `ReplyContext` must **wait for all** required legs for Batch (no short-circuit on the first
  failure) — add a strategy flag (e.g. `IAckStrategy.WaitsForAllLegs => true` for Batch) that
  `ReplyContext` honors before firing.
- Remove the `AckShape.Batch` fail-fast guard in `ContractOptionsValidator`.

## Content-routing classifier stage — `RouteWhen` facts producer

**Status:** the `RouteWhen` routing mechanism is IMPLEMENTED (per-output `RouteWhen` content filter;
fan-out selects source-applicable ∩ RouteWhen-matching legs; no-match = filtered drop). What's NOT
built yet is the concrete **classifier `IMessageStage`** that PRODUCES the facts a `RouteWhen` matches.

**What it must do:**
- Live in the format module (e.g. `Formats.Hl7`, so Core stays content-agnostic), parse the message,
  and write domain facts into `MessageContext.Headers` in a documented, per-format vocabulary
  (e.g. `hl7.messageType`, `hl7.triggerEvent`, `hl7.sendingApp`, `priority`, `patientClass`).
- Register like `PassThroughStage` (module-owned `AddHl7Stages()`), and be referenced by name in a
  catalog pipeline so the FSE's `RouteWhen` rules have facts to match.
- Never reference outputs — it only classifies; the FSE binds facts → outputs via `RouteWhen`.

**Notes:** document the fact vocabulary as a stable contract (it's the one coupling surface between the
developer's classifier and the FSE's `RouteWhen`). Use a header-key prefix convention (e.g. `hl7.*`) to
avoid collisions with transport headers. Matching today is AND + exact ordinal equality; value-lists /
wildcards / a centralized routing table are possible later extensions.

## TCP outbound — proactive stale-connection detection (Option 2) — LOW PRIORITY

**Status:** NOT implemented. **Option 1 is done** (`TcpOutboundEndpoint` reconnects once on a reused
connection that fails; `TcpConnectionPool.RentAsync` returns a `reused` flag + fixes the slot-leak on a
failed dial). Option 1 is the correctness guarantee; Option 2 below is a pure latency optimization that
reduces how often the reconnect-once path (and its one slow reconnect) is hit. Layer it **under** Option 1,
never as a replacement — Poll is a point-in-time check and can't close the race, so the reconnect stays.

**What it must do:** before handing out a pooled connection, drop the ones the peer already closed, so a
resumed burst rarely pays the reconnect penalty.
- **Liveness probe on rent (always-on, free):** in `TcpConnectionPool`, gate each dequeued idle connection
  with `socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0` (⇒ pending FIN/RST = dead ⇒ dispose
  and try the next / dial fresh). `Available > 0` in a request-reply pool = leftover/desynced bytes ⇒ also
  discard (restores the "fresh stream per message" safety the legacy connection-per-message had).
- **Idle eviction (opt-in, off by default):** stamp each entry with `ReturnedTimestamp` on `Return`; on rent,
  discard entries idle longer than a configured `IdleReuseTimeoutSeconds` (`TcpOutboundOptions`, nullable).
  Prefer **on-rent** eviction over a background sweeper (no extra timer/thread, no rent race).

**Perf / outside-world caveats (see the architecture §3.7 stale-connection bullet):**
- Poll = one non-blocking syscall/rent, no network I/O, no bytes sent → negligible; cannot regress downstream interop.
- Idle eviction trades connection reuse for staleness-avoidance: too-aggressive a threshold pushes back toward
  connection-per-message (more handshakes, more downstream connect/close churn, TIME_WAIT risk — the very costs
  pooling removed vs. the legacy `TcpSender`). Keep it above the in-burst inter-message gap, below the downstream
  idle-close; leave it **off** by default and rely on the free Poll probe.

**Tests (when built):** healthy idle conn → reused; peer-closed conn → Poll detects → dialed fresh (no failed
send); `Available > 0` → discarded; small `IdleReuseTimeoutSeconds` → rented-after-threshold dials fresh; draining
multiple stale entries doesn't leak semaphore slots. Then flip the §3.7 doc note from "future refinement" to present tense.
