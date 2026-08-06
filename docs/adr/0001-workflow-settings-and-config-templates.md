# ADR 0001 — Workflow-based configuration delegation (the "Settings" model) + config template folder

- **Status:** Proposed
- **Date:** 2026-08-03
- **Deciders:** IBE Agent engine team
- **Supersedes:** the fixed `Catalog.Templates` split (rename + extension; see "Naming" below)
- **Related:** `docs/architecture/Refactor_ArchitectureDoc_v4.md` §8 (Contract/Catalog model), §3.7/§3.7a (template resolution)

> This ADR captures the design agreed during the 2026-08 configuration-flexibility discussion.
> It is a **design record only** — no engine code has been changed yet. Illustrative JSON below is
> **proposed** shape, not current shape.

---

## 1. Context

The engine has **two configuration audiences**:

- **Developers** own *mechanism / code*: pipeline stages, codecs, formats. Authored in `catalogData.json`.
- **Field Service Engineers (FSE)** own *physical topology and operational policy*: inbound/outbound
  endpoints, which inputs feed which outputs, acknowledgement/retry/channel tuning. Authored in
  `contractData.json`.

Today the ownership split is **static and hardcoded**: every field belongs permanently to one side.
The only negotiated knob is `ReplyOnFilter` (dev supplies a default, FSE may override), implemented as
a one-off `contract.ReplyOnFilter ?? template.ReplyOnFilter ?? false` in `ContractTemplateResolver`.

Four problems motivated this ADR:

1. **Developers want to selectively expose knobs.** Per blueprint, a dev should decide *which*
   operational parameters an FSE may set — and lock the rest — rather than the split being global and fixed.
2. **Some stages need FSE-supplied resources.** Example: a dev-selected HL7 *filter* stage needs a
   **ruleset file**. The dev owns the stage; the FSE must supply (or at least see) the file it reads.
3. **The FSE surface must stay simple.** An FSE should, at most, name a blueprint and tweak a few
   clearly-labelled values. Internal field paths, stage names, and policy vocabulary must not leak to them.
4. **Naming clash + monolithic docs.** "Template" is overloaded, and the single annotated `template.json`
   is hard to navigate.

### Design principles

- **Mechanism vs policy vs delegated-policy.** Dev owns mechanism; FSE owns topology/operational policy;
  this ADR adds a third axis — the dev *delegating a bounded slice of policy* to the FSE.
- **Push complexity to the party equipped for it.** All wiring/indirection lives on the (technical) dev
  side; the FSE side is a flat, friendly, pre-defaulted form.
- **Least privilege by default.** The FSE can only touch what the dev deliberately exposed.
- **Fail-fast, plain-language validation** (consistent with the repo's fail-fast config convention).
- **Simplicity is a first-class quality**, weighed equally with capability.

---

## 2. Decisions

| # | Decision | Rationale |
|---|----------|-----------|
| D1 | Rename the FSE-facing blueprint **`Template` → `Workflow`** everywhere. | Removes the overloaded "template" term; frees "template" for the copy-paste starter files (D7). |
| D2 | Introduce the **`Settings` model**: the Workflow author declares a flat list of friendly, named settings; the FSE fills them like a form. | One simple FSE concept; the dev designs the form. |
| D3 | The contract references a workflow as **string-or-object** (`"Workflow": "adt"` **or** `"Workflow": { "Use": "adt", "Settings": { … } }`). | Keeps the zero-config case one line; groups settings with the workflow (cohesion); leaves a free future upgrade path. |
| D4 | **Multiple workflows per contract = YAGNI.** | A contract is *one shared pipeline, run once, then fan out* (§8); multiple workflows would break that core model. If ever needed, it is more contracts, not more workflows. |
| D5 | **Resource/file parameters** (e.g. a filter ruleset) are just a `Setting` of `Kind: file`; the FSE sees a plain path (or a named choice). | The file case is uniform with every other setting — no special syntax for the FSE. |
| D6 | **Dropped:** Profiles (deferred), path-addressed "Knobs" tables, and the Locked/Open/Required vocabulary *on the FSE side*. | The mode vocabulary collapses to "is it a Setting or not" (see §3.2); Profiles add value but are not needed now. |
| D7 | Replace `template.json` with a **generated** `config/templates/{endpoints,workflows,contracts}/` tree of commented, copy-paste starters. | FSE never authors from scratch; generated from the same metadata that drives validation, so it cannot drift. |
| D8 | One code seam is implied: **`ComponentRegistry.CreateStage(name)` → `CreateStage(name, StageParameters)`.** | Stages must receive their bound parameters at construction (mirrors how codecs already take `CodecOptions`). |

---

## 3. The model in detail

### 3.1 Three zones (only the middle is negotiable)

```
Dev-only (mechanism)      |  Negotiable middle (delegated policy)     |  FSE-only (topology)
--------------------------|-------------------------------------------|-----------------------
pipeline stages           |  Acknowledgement / Response               |  Inputs / endpoints
codecs / formats          |  Retry / DeliveryGuarantee                |  Outputs / FromInputIds
                          |  Channel (capacity/DOP/overflow)          |  physical wiring
                          |  stage parameters (incl. resource files)  |
```

- The **left** zone is the dev's by definition (an FSE cannot assemble stages).
- The **right** zone is the FSE's by definition (the dev cannot know a site's ports/hosts/wiring).
- Only the **middle** is where "the dev decides which knobs the FSE gets" is meaningful. `Settings` target
  the middle only. This preserves the clean `dev = code / FSE = topology` boundary.

### 3.2 Why the model is simple: the mode vocabulary collapses

There is no Locked/Open/Required enum on the FSE side. There is one idea:

- **It is a Setting** → the dev exposed it. It has a friendly name and (usually) a default; the FSE may override it.
- **It is not a Setting** → it is constant and *invisible* to the FSE.

Consequently:
- "Locked by default" is automatic — the FSE cannot reference what is not in their vocabulary.
- "Required" is simply *a Setting with no default*.
- "Open" is *a Setting with a default*.

### 3.3 Dev side — the `Settings` declaration (on the Workflow)

Each Setting is: a **friendly key**, a `Description`, an optional `Default`, optional **guardrails**
(`Min`/`Max`/`Allowed`/regex), and a hidden **`Bind`** that says where the value actually goes. Optional
`Kind` (`file`, `secret`, …) and `Scale` (unit conversion) handle non-scalar/units cases. **`Bind` is
optional** — if omitted, the key *is* the target field.

`Bind` targets, all invisible to the FSE:
- a contract field: `Acknowledgement.TimeoutMs`
- a per-output field (wildcard): `Outputs[].Retry.MaxAttempts`
- a stage parameter / resource: `stage:hl7-filter.Ruleset`

### 3.4 FSE side — the `Settings` bag

Flat `key: value`. Every setting has the **same shape** whether it targets an ack timeout, a retry
count, or a filter file — the FSE never knows which is which. The whole block is **omittable** (omit →
all defaults). No paths, no stage names, no modes.

### 3.5 Resolution flow (single enforcement point)

The renamed `ContractWorkflowResolver` does, in order:

1. Resolve the workflow by name (fail-fast if unknown).
2. Validate every FSE-set setting against the Workflow policy (type, min/max, allowed, exists);
   errors are **plain-language, keyed by the friendly name** — e.g.
   *"Setting 'AckTimeoutSeconds' must be between 5 and 60 (got 90)."*
3. Fill defaults for exposed-but-unset settings.
4. For `Kind: file`, resolve the reference to a physical path, check `ContentType` (and ideally a
   checksum), and **validate the path is inside an allowed root** (see §3.7 Security).
5. Apply each value to its `Bind` target (contract field or stage parameter) with any `Scale`.
6. Produce a fully-resolved contract + a **resolved manifest** listing the effective resource paths per
   contract (discoverability / ops).

The Workflow and pipeline definitions are **never mutated** — only the bound values differ per contract.

### 3.6 The resource/file case, concretely

A dev-selected `hl7-filter` stage needs a ruleset file:

- The dev ships a **default** file (a `Resources` entry) so the FSE can inspect/copy it.
- The FSE either uses the default, or points the setting at their **own** file (a path string), or the
  dev constrains it to named choices so the FSE picks a word instead of a path.
- The resolver binds the chosen file into the compiled `hl7-filter` stage; the Workflow stays intact.

### 3.7 Security

FSE-supplied file paths are untrusted input. The resolver MUST restrict resolution to an allowed
root (a configured resources directory) and reject traversal / absolute escapes — otherwise a contract
could name an arbitrary file for read. `Kind: secret` values resolve from the secret store and are
**never** inlined or written to the resolved manifest.

### 3.8 Governance / versioning

A locked value or a changed default is a **fleet-wide lever** — every contract on that workflow moves
at once. Workflows should carry a `Version` (and change review) so such changes are deliberate and auditable.

---

## 4. Config examples (proposed shape)

### 4.1 `catalogData.json` (dev)

```jsonc
{
  "Catalog": {
    "Codecs":  { "hl7v2": { "Type": "hl7v2" }, "avro-zip": { "Type": "avro-zip" } },
    "Formats": { "hl7-standard": { "Codec": "hl7v2", "BatchCodec": "avro-zip" } },

    // Shared pipeline = ordered stage names (a pure code artifact; no policy here).
    "Pipelines": { "adt-standard": [ "hl7-parse", "hl7-filter", "hl7-enrich" ] },

    // Named resources the stages consume. Each ships a DEFAULT file the FSE can inspect/replace.
    "Resources": {
      "adt-default-rules": { "ContentType": "application/vnd.ibe.filter-rules+json", "Ref": "resources/adt-default.rules.json" }
    },

    // Workflows = the ONE FSE-facing blueprint. The dev designs the FSE's form here.
    "Workflows": {
      "adt": {
        "Version": 1,
        "Pipeline": "adt-standard",
        "Format":   "hl7-standard",

        // Anything NOT listed here is constant and invisible to the FSE.
        "Settings": {
          "AckTimeoutSeconds": {
            "Description": "Seconds to wait for downstream delivery before NACK.",
            "Default": 30, "Min": 5, "Max": 60,
            "Bind": "Acknowledgement.TimeoutMs", "Scale": 1000     // FSE thinks in seconds; engine stores ms
          },
          "MaxRetries": {
            "Description": "Delivery attempts before giving up.",
            "Default": 3, "Min": 1, "Max": 5,
            "Bind": "Outputs[].Retry.MaxAttempts"
          },
          "FilterRules": {
            "Description": "Ruleset file deciding which messages are dropped.",
            "Kind": "file",
            "ContentType": "application/vnd.ibe.filter-rules+json",
            "Default": "adt-default-rules",                        // dev-shipped; FSE may point elsewhere
            "Bind": "stage:hl7-filter.Ruleset"
          }
        }
      }
    }
  }
}
```

### 4.2 `contractData.json` (FSE)

```jsonc
{
  "Endpoints": { /* FSE topology (TCP/HTTP in/out) — unchanged */ },

  "Contracts": [
    {
      "Name": "adt-fanout-siteA",

      // String-or-object. Bare string = all defaults. Object = tuned.
      "Workflow": {
        "Use": "adt",
        "Settings": {
          "AckTimeoutSeconds": 45,
          "FilterRules": "site-a/adt.rules.json"    // FSE's own file (copied from the default & edited)
          // MaxRetries omitted -> default 3
        }
      },

      "Inputs":  [ { "InputId": 1 } ],
      "Outputs": [ { "OutputId": 102, "Required": true } ]
    }
  ]
}
```

Zero-config equivalent when no overrides are needed:

```jsonc
{ "Name": "adt-fanout-siteA", "Workflow": "adt", "Inputs": [ { "InputId": 1 } ], "Outputs": [ { "OutputId": 102 } ] }
```

### 4.3 `config/templates/` tree (replaces `template.json`)

```
config/
  catalogData.json          # dev (live)
  contractData.json         # FSE (live)
  templates/                # AUTHORING AIDS — never loaded by the engine
    endpoints/              #   one commented example per endpoint kind
      tcp-inbound.jsonc  http-inbound.jsonc  tcp-outbound.jsonc  http-outbound.jsonc  cim-s3-outbound.jsonc
    workflows/              #   one copy-paste "Workflow" block per workflow (GENERATED from Settings)
      adt.jsonc  lab-query.jsonc
    contracts/              #   full starter contracts for common scenarios
      passthrough.jsonc  cim-s3-fanout.jsonc  request-reply.jsonc
```

- The three folders **compose**: `endpoints/*` (smallest) → `workflows/*` (one workflow's settings block)
  → `contracts/*` (a full contract wiring endpoints + a workflow). `contracts/*` are the "start here" files.
- **Generate** `workflows/*` and `endpoints/*` from existing metadata (the `Settings` declaration and the
  endpoint option shapes) via a small `gen-templates` step, so they never drift.
- **Comments are safe:** the .NET config loader already tolerates `//` comments and trailing commas
  (today's `catalogData.json` uses them), so a template can be copied verbatim into a live file.

Example generated `templates/workflows/adt.jsonc`:

```jsonc
// Paste into a contract's "Workflow" field to use the 'adt' workflow.
"Workflow": {
  "Use": "adt",
  "Settings": {
    "AckTimeoutSeconds": 30,            // 5..60. Wait before NACK.
    "MaxRetries": 3,                    // 1..5.
    "FilterRules": "adt-default-rules"  // file: copy the default, edit, point here.
  }
}
```

---

## 5. Consequences

### Positive
- **Radically simple FSE surface:** name a workflow, optionally tweak a flat, labelled, defaulted list.
- **Dev control with guardrails:** expose exactly the knobs you want, with ranges/allowed values.
- **Uniform resource handling:** files are just settings; dev defaults are discoverable and overridable.
- **One source of truth:** the `Settings` declaration drives validation **+** the WebAgent form **+** the
  generated copy-paste templates — no drift.
- **Preserves the boundary:** `dev = code / FSE = topology` stays intact.
- **Fail-fast, friendly errors** keyed by human names.

### Negative / costs
- **Dev-side authoring cost:** the dev writes the `Settings` declaration (bindings, constraints) once per
  workflow. Deliberate trade — complexity sits with the technical party.
- **New resolver/validator work:** binding, constraint checks, resource resolution, path security, manifest.
- **One code seam churn:** `CreateStage(name, params)` and a stage parameter-schema mechanism.
- **A generator to build/maintain** for the templates tree.

### Neutral
- Live config remains two files (`catalogData.json`, `contractData.json`); templates are additive docs.

---

## 6. Alternatives considered

1. **Plain allow-list of overridable fields** — no defaults, no guardrails; strictly weaker than Settings.
2. **ARM/Helm-style `${param}` substitution in the workflow body** — heavier indirection; unnecessary once
   settings bind directly to fields/stage params. Reconsider only if a knob maps to *no* real field.
3. **Profiles (one dial → many knobs)** — genuinely useful; **deferred** (D6), trivial to add later as a
   setting whose value expands to several binds.
4. **Ownership inversion / role-binding ("revolutionary")** — the Workflow declares the *shape* (output
   roles + required formats) and the contract binds physical endpoints to roles. Powerful for reuse, but it
   pulls **topology** into the dev-owned workflow, eroding the `dev = code / FSE = topology` boundary.
   **Not adopted** — reward not proportional to the cost/coupling. Explored separately as a follow-up study;
   the `templates/` tree (D7) delivers most of its "start from a proven shape" benefit with none of the machinery.

---

## 7. Open questions / future work

- Exact `Bind` path grammar (per-output wildcard `Outputs[]`, per-OutputId targeting for asymmetric legs).
- Resource supply styles: external `Ref` vs small **inline** content vs dev default; `secret` resolution.
- Stage parameter-schema mechanism (attribute/manifest) and the `CreateStage(name, params)` signature.
- Whether the live config loader keeps comments in FSE files or strips on copy (team preference).
- `gen-templates` ownership (build target vs CLI) and where the resolved manifest is emitted for ops.

---

## 8. Rollout (phased)

1. **Rename** `Template → Workflow` (mechanical; namespaces flat, low risk).
2. **Settings core:** declaration + resolver binding + fail-fast validation; fold `ReplyOnFilter` into it.
3. **Resources:** `Kind: file`, dev defaults, path security, resolved manifest; `CreateStage(name, params)`.
4. **Templates tree + generator**; retire `template.json`.
5. (Later) Profiles; WebAgent form generation from `Settings`.
