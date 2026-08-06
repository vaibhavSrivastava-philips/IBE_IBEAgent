# Vaibhav — IBE Agent TODO / deferred items

Running list of design items intentionally deferred, with enough detail to pick up later.

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
