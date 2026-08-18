# ADR 0002 — Workflow topology governance (constraint-based slots)

- **Status:** Proposed
- **Date:** 2026-08-04
- **Deciders:** IBE Agent engine team
- **Extends:** `docs/adr/0001-workflow-settings-and-config-templates.md` (the `Workflow` + `Settings` model)
- **Related:** `docs/architecture/Refactor_ArchitectureDoc_v4.md` §8 (Contract/Catalog model), §3.3/§3.4 (fan-out, `FromInputIds`)

> Design record only — no engine code has been changed. Illustrative JSON is **proposed** shape.
> This ADR resolves the one open axis ADR 0001 left unaddressed: **how much of the contract's
> topology (inputs/outputs) a workflow may govern.**

---

## 1. Context

ADR 0001 introduced the **`Workflow`** blueprint and the **`Settings`** delegation model: the dev
exposes a flat, defaulted, guard-railed set of *scalar* knobs (ack timeout, retry count, a filter
ruleset file, …) that the FSE fills like a form. Everything the dev does not expose stays constant.

ADR 0001 deliberately left **topology** (how many inputs/outputs, of what transport, with what
per-leg delivery semantics) entirely FSE-owned, and recorded two extreme alternatives:

- **A — Current + Settings (ADR 0001 baseline):** the FSE authors the full topology inline; the
  workflow governs only scalar settings + pipeline/format. Maximum FSE flexibility, **no topology
  governance** (sites can drift; the dev cannot constrain the shape).
- **C — Ownership inversion (role-binding):** the workflow declares an exact named shape (roles
  `ehr`/`archive`) and the contract only binds endpoints to roles. Maximum reuse/consistency, but it
  pulls topology structure into the dev side, is rigid for heterogeneous sites, and is a large re-model.

Neither extreme is right for a product with **variable-but-patterned** deployments: sites differ in
count and wiring, yet the dev still wants to bound the shape (e.g. "ADT in over TCP, fan out to 1..8
HL7 legs, delivery guarantee your choice within {AtMostOnce, AtLeastOnce}").

---

## 2. Decision (summary)

Adopt **B — constraint-based topology governance**: apply ADR 0001's delegation model to **topology
itself**. The workflow declares *topology constraints* (cardinality + per-slot knob policy, including
transport **kind**); the FSE authors the actual endpoints as today and references them by id; the
resolver enforces the constraints and fills defaults.

The pivotal insight: **A and C are not competing designs — they are the two ends of a single dial,
"how much topology the workflow locks down." B is that dial.**

| Workflow locks… | …you get |
|---|---|
| **nothing** (open cardinality, all knobs open, no `Kind`) | today's manual mode = **A** |
| **some** (cardinality bounds + a few locked knobs + defaults + `Kind`) | **governed flexibility (this ADR)** |
| **everything** (fixed cardinality, all knobs locked) | ≈ the role-binding inversion = **C** |

Consequences of this framing:
- B **subsumes** A (the fully-open corner) and **approximates** C (the fully-locked corner), so we
  never maintain two models.
- "Manual mode" stops being a separate escape hatch — it is simply *a contract referencing the open
  `passthrough` workflow*.
- Option C is **retired as a separate future re-model**; if fleet uniformity ever becomes a felt
  need, tighten a B workflow.

| # | Decision | Rationale |
|---|----------|-----------|
| E1 | Workflow declares **topology constraints**: `Inputs`/`Outputs` with `Min`/`Max` and a per-slot knob policy. | Bounds the shape without owning the wiring. |
| E2 | Slot knobs reuse the **ADR 0001 policy descriptor**: `{ "Value": x }` = locked; `{ "Default": …, "Allowed"/"Min"/"Max" }` = open; `{ "Open": true }` = open/free; no default & no value = required. | One vocabulary, two places (scalar settings + slots). No new language. |
| E3 | Add a **`Kind`** slot constraint (transport): `{ "Value": "tcp" }` locked, `{ "Allowed": [...] }` open set, `{ "Value": "any" }` **or key omitted** = any. | Lets a workflow require/limit transport; validated against the referenced endpoint. |
| E4 | The **`Kind` set is extensible**: `tcp`, `http`, `file`, `cim-s3`, … (any registered transport), plus `any`. | New endpoint transports must be constrainable without schema churn. |
| E5 | The **contract keeps today's shape**: endpoints declared physically under `Endpoints`, referenced by id in `Inputs`/`Outputs`; only *open* knobs are supplied. | FSE experience ≈ unchanged (or simpler, via defaults). Preserves `dev = code / FSE = topology`. |
| E6 | Ship a built-in **`passthrough`** = maximally-open workflow (no pipeline, no `Kind`, all knobs open). | Makes "no governance" a first-class named choice, not a code path. |
| E7 | **Named slot groups are NOT adopted now** (deferred). The flat single-group form is the design. | YAGNI; the flat form covers current needs. See §7. |

---

## 3. The model in detail

### 3.1 Two layers: topology *contract* vs topology *instance*

- **Topology contract (dev, in the workflow):** the *constraints* — cardinality (`Min`/`Max`),
  accepted transport (`Kind`), and for each slot which knobs (`Required`, `Format`,
  `DeliveryGuarantee`, `FromInputs`, `Channel`, …) are **locked** vs **open (with default/allowed)**.
- **Topology instance (FSE, in `contractData.json`):** the actual endpoints (declared as today) and,
  per referenced output, only the **open** knobs. Locked knobs are simply absent from the FSE file.

### 3.2 Slot policy vocabulary (identical to ADR 0001 settings)

| Descriptor | Meaning | FSE effect |
|---|---|---|
| `{ "Value": x }` | Locked to `x` | Cannot set it; setting it → validation error |
| `{ "Default": d, "Allowed": [...] }` / `{ "Default": d, "Min": …, "Max": … }` | Open with default + guardrails | Optional; absent → `d`; out-of-range → error |
| `{ "Open": true }` | Open, unconstrained | Optional free value |
| *(no `Value`, no `Default`)* | Required | Missing → validation error |

### 3.3 The `Kind` constraint (E3/E4)

`Kind` is a **validated binding constraint**, not a value the FSE types. The FSE references an
endpoint id; the endpoint's transport is implied by the `Endpoints` array it lives in
(`TcpInbound`/`HttpOutbound`/…). At resolve time the engine checks the bound endpoint's transport
against the slot's `Kind`:

- `{ "Value": "tcp" }` → only TCP endpoints may fill the slot.
- `{ "Allowed": ["tcp", "http"] }` → either.
- `{ "Value": "any" }` **or the `Kind` key omitted** → any transport (used by `passthrough`).

The accepted set is **open/extensible** — `tcp`, `http`, `file`, `cim-s3`, and any future registered
transport, plus `any`.

### 3.4 Resolution / validation flow (single enforcement point)

`ContractWorkflowResolver` (renamed per ADR 0001) additionally:

1. Enforces **cardinality**: input/output counts within `Min`/`Max` (plain-language errors, e.g.
   *"Workflow 'adt-fanout' allows 1..8 outputs; contract 'siteA' declares 9."*).
2. Enforces **`Kind`**: each bound endpoint's transport ∈ the slot's allowed kinds
   (*"…input binds endpoint 7 (http) but workflow requires kind 'tcp'."*).
3. Applies each slot knob: inject **locked** values, default **open-unset** knobs, validate FSE-set
   **open** knobs against `Allowed`/`Min`/`Max`, reject FSE attempts to set **locked** knobs.
4. Reuses existing referential checks (`FromInputs` must resolve to a declared input — today's
   `FromInputIds` validator).

The workflow/pipeline definitions are never mutated; only bound values differ per contract.

### 3.5 `passthrough` = manual mode (E6)

`passthrough` locks nothing: `Pipeline: null`, no `Kind`, open cardinality, all slot knobs `Open`.
A contract that references it is exactly today's fully-manual contract — so there is one model, and
"manual" is just its most permissive point.

---

## 4. Config examples (proposed shape)

### 4.1 `catalogData.json` (dev)

```jsonc
{
  "Catalog": {
    "Codecs":  { "hl7v2": { "Type": "hl7v2" }, "avro-zip": { "Type": "avro-zip" }, "raw": { "Type": "raw" } },
    "Formats": { "hl7-standard": { "Codec": "hl7v2", "BatchCodec": "avro-zip" }, "raw-passthrough": { "Codec": "raw" } },
    "Pipelines": { "adt-standard": [ "hl7-parse", "hl7-filter", "hl7-enrich" ] },
    "Resources": { "adt-default-rules": { "ContentType": "application/vnd.ibe.filter-rules+json", "Ref": "resources/adt-default.rules.json" } },

    "Workflows": {

      // Governed shape: ADT in over TCP, fan out to 1..8 TCP/HTTP legs.
      "adt-fanout": {
        "Version": 1,
        "Pipeline": "adt-standard",

        "Inputs": {
          "Min": 1, "Max": 4,
          "Slot": {
            "Kind":    { "Value": "tcp" },                               // locked: ADT arrives over TCP only
            "Format":  { "Value": "hl7-standard" },
            "Channel": { "DegreeOfParallelism": { "Value": 1 },          // ordered HL7 -> DOP locked
                         "Capacity": { "Default": 1024, "Min": 256, "Max": 8192 } }
          }
        },

        "Outputs": {
          "Min": 1, "Max": 8,
          "Slot": {
            "Kind":              { "Allowed": ["tcp", "http", "cim-s3"] },   // extensible transport set
            "Required":          { "Value": true },
            "Format":            { "Default": "hl7-standard", "Allowed": ["hl7-standard", "raw-passthrough"] },
            "DeliveryGuarantee": { "Default": "AtLeastOnce", "Allowed": ["AtMostOnce", "AtLeastOnce"] },
            "FromInputs":        { "Open": true }
          }
        },

        "Acknowledgement": { "Shape": "Single", "Enhanced": true },
        "Settings": {
          "AckTimeoutSeconds": { "Default": 30, "Min": 5, "Max": 60, "Bind": "Acknowledgement.TimeoutMs", "Scale": 1000 },
          "FilterRules":       { "Kind": "file", "Default": "adt-default-rules", "Bind": "stage:hl7-filter.Ruleset" }
        }
      },

      // Maximally-open workflow == today's manual mode. No pipeline, no Kind key (any transport),
      // every knob open.
      "passthrough": {
        "Pipeline": null,
        "Inputs":  { "Min": 1, "Max": 32, "Slot": { "Format": { "Open": true } } },
        "Outputs": { "Min": 1, "Max": 32, "Slot": {
            "Required":          { "Open": true },
            "Format":            { "Open": true },
            "DeliveryGuarantee": { "Open": true },
            "FromInputs":        { "Open": true }
        } }
      }
    }
  }
}
```

### 4.2 `contractData.json` (FSE)

```jsonc
{
  "Endpoints": {
    "TcpInbound":  [ { "SourceEndpointId": 1, "Port": 5101, "Format": "hl7v2" } ],
    "TcpOutbound": [ { "OutputId": 102, "Host": "127.0.0.1", "Port": 5201, "ExpectReply": true } ],
    "HttpOutbound":[ { "OutputId": 2, "Endpoint": "http://ehr/ibe/inbound", "ContentType": "application/octet-stream" } ]
  },

  "Contracts": [
    {
      "Name": "adt-fanout-siteA",
      "Workflow": { "Use": "adt-fanout", "Settings": { "AckTimeoutSeconds": 45 } },

      "Inputs":  [ 1 ],                                    // id 1 is TcpInbound -> satisfies Kind:tcp
      "Outputs": [
        { "Id": 102 },                                     // TcpOutbound  -> Kind ok; defaults apply
        { "Id": 2, "DeliveryGuarantee": "AtMostOnce", "FromInputs": [ 1 ] }   // HttpOutbound -> Kind ok
      ]
    }
  ]
}
```

Zero-config equivalent (no overrides): `"Workflow": "adt-fanout"` and outputs `[ { "Id": 102 } ]`.

---

## 5. Cost analysis

**Conceptual**
- **FSE:** ≈ unchanged from today (still list inputs/outputs by id with knobs) — in practice *lower*,
  because defaults reduce typing and validation catches shape mistakes early. **LOW.**
- **Dev:** authors cardinality + per-slot policy (incl. `Kind`) on top of `Settings`, reusing one
  descriptor vocabulary. **MODERATE, one-time per workflow.**

**Implementation**
- **Over ADR 0001 (A):** additive. New workflow DTO fields (`Inputs`/`Outputs` `{ Min, Max, Slot }`);
  resolver/validator gains cardinality + `Kind` + slot locked/open/default enforcement. The **contract
  shape barely changes** (today's Inputs/Outputs by id). **MODERATE.**
- **Versus C (inversion):** far cheaper — no `ContractOptions` re-model, no role concept, no binding
  indirection, no WebAgent authoring-flow rewrite. **A fraction of C's cost.**

**Risk:** **LOW–MODERATE.** Additive over the existing topology + the ADR 0001 resolver. No breaking
re-model.

**Migration:** **LOW.** Existing contracts remain valid as instances of an open workflow; the shape is
backward-compatible (A is the no-lock corner of B).

**Complexity creep:** **CONTAINED**, provided (1) slot knobs reuse the one Settings descriptor — do not
invent a second, and (2) cross-slot invariants stay a tiny fixed set, not a rules engine.

---

## 6. Consequences

### Positive
- **Topology governance without taking wiring from the FSE** — dev owns the *rules*, FSE owns the *wiring*.
- **Transport safety** — `Kind` prevents binding the wrong endpoint type into a slot.
- **Defaults + fail-fast validation** shrink and de-risk the FSE artifact.
- **One model on a continuum** — A and C are its extremes; manual mode is the open corner.
- **Retires option C** as a separate future re-model (and its "maintain two models" con).
- **Reuses ADR 0001** wholesale (Settings descriptor, resolver, fail-fast, plain-language errors).

### Negative / costs
- Dev-side authoring effort per workflow (cardinality + slot policy).
- New resolver/validator logic (cardinality, `Kind`, slot policy, object-valued locked knobs).

### Neutral
- Live config remains two files; endpoints are still declared physically by the FSE.
- Weaker *enforced uniformity* than C (sites may still differ within the allowed envelope) — by design;
  tighten the workflow (fixed cardinality + locked knobs) to approach C when uniformity is wanted.

---

## 7. Alternatives considered

1. **A — ADR 0001 baseline (no topology governance):** now expressible as the fully-open corner of B
   (the `passthrough` workflow). Not a separate model.
2. **C — Ownership inversion / role-binding:** the fully-locked corner of B. Not adopted as a distinct
   design; reachable by tightening a B workflow. Its unique extra (named roles, *enforced* identical
   shape) is deferred and recoverable via named slot groups (below) if ever needed.
3. **Named slot groups (E7 — DEFERRED):** keyed input/output groups, each with its own `Kind` +
   cardinality + slot policy (e.g. "one TCP `primary` **and** an optional HTTP `audit`"), with the FSE
   binding by group name. Powerful for heterogeneous slots and a non-forced bridge toward C, but **not
   needed now**. The flat single-group form is the current design; groups can be added additively later
   without breaking it.

---

## 8. Open questions / future work

- **Named slot groups** (§7) — when a workflow genuinely needs distinct kinds simultaneously.
- **Per-kind cardinality** within a group (e.g. "1 tcp + 0..2 http") — likely folds into slot groups.
- **Cross-slot invariants** — a minimal fixed set (e.g. "≥1 AtLeastOnce output", "≤1 reply-capable
  output"); explicitly *not* a general constraint DSL.
- **Object-valued locked knobs** (e.g. a locked `Batching { … }`) — resolver deep-merge/compare rules.
- **`Channel` slot policy scope** — which per-input isolation knobs are lockable vs open by default.

---

## 9. Rollout (relative to ADR 0001)

1. ADR 0001 phases 1–3 first (rename, Settings core, resources).
2. **This ADR:** add topology-constraint DTOs (`Inputs`/`Outputs` `{ Min, Max, Slot }`, `Kind`),
   resolver cardinality + `Kind` + slot-policy enforcement, and the built-in `passthrough` workflow.
3. Regenerate the `config/templates/` tree (ADR 0001 D7) to reflect governed slots.
4. (Later, only if needed) named slot groups (§7).
