# Catalog & Workflow Authoring Guide (`catalogData.json`)

This is the **developer** guide for authoring `config/catalogData.json` — the "plug‑and‑play code"
building blocks that Field Service Engineers (FSEs) reference **by name** from `contractData.json`.

- **You (developer)** own `catalogData.json`: pipelines, codecs, formats, **workflows**, resources.
- **The FSE** owns `contractData.json`: which endpoints exist and which inputs feed which outputs,
  plus the handful of knobs **you deliberately expose** to them via a workflow's `Settings`.

> Reference implementation: `ADR 0001` (`docs/adr/0001-workflow-settings-and-config-templates.md`).
> Copy‑paste starters live in `config/templates/` (and `workflows/*.jsonc` are generated — see the end).>
> **Sibling references:** endpoint keys -> [../endpoints/README.md](../endpoints/README.md) ·
> contract keys -> [../contracts/README.md](../contracts/README.md).

### Using a generated workflow block (the FSE side)

The `*.jsonc` files in **this** folder are **generated, ready-to-paste** `Workflow` blocks — one per
workflow defined below. Paste one into a contract's `"Workflow"` field and fill only the `Settings` you
want to change (each is pre-filled with its default and a guardrail comment):

```jsonc
// contractData.json -> Contracts[]
"Workflow": { "Use": "adt", "Settings": { "AckTimeoutSeconds": 45 } }   // omit Settings for all defaults
```

The rest of this page is the **developer** side: how to *author* the workflows those blocks come from.

---

## 1. File shape

Everything is under a single `"Catalog"` root:

```jsonc
{
  "Catalog": {
    "Codecs":    { /* name -> { Type, Params } */ },
    "Pipelines": { /* name -> [ "stage-name", ... ] */ },
    "Formats":   { /* name -> { Codec, BatchCodec? } */ },
    "Workflows": { /* name -> { Pipeline?, Format? | Formats?, Version?, Settings? } */ },
    "MediaTypes":{ /* ".ext" -> "media/type"  (for the media-type stage) */ },
    "Resources": { /* name -> { ContentType?, Ref } */ }
  }
}
```

`//` comments and trailing commas are allowed (the loader tolerates them).

The four layers reference each other **by name**, each building on the previous:

```
Codecs  <-  Formats  <-  Workflows        Pipelines <- Workflows        Resources <- (a Kind:file Setting)
```

---

## 2. The building blocks

### 2.1 `Codecs` — how bytes are encoded
A named binding of a **registered codec `Type`** + optional `Params`.
```jsonc
"Codecs": {
  "hl7v2":  { "Type": "hl7v2" },     // pass-through HL7 v2 (one message -> bytes)
  "base64": { "Type": "base64" }     // decodes a base64 payload to raw bytes
}
```
`Type` must be a codec registered in code (`hl7v2`, `base64` today). Naming lets you reuse one type
with different params.

### 2.2 `Pipelines` — the shared processing stages
An **ordered list of stage names**, run **once** per message before fan‑out. Every stage name must be
registered in code. Empty pipelines are rejected.
```jsonc
"Pipelines": {
  "main": [ "passthrough" ],
  "blob": [ "blob-envelope-extract" ]
}
```
Registered stages today: `passthrough`, `blob-envelope-extract`, `media-type`, `hl7-classify`.

### 2.3 `Formats` — a per‑leg encoding bundle
A message `Codec` + an optional `BatchCodec` (both names of `Codecs` entries).
```jsonc
"Formats": {
  "hl7-standard": { "Codec": "hl7v2", "BatchCodec": "avro-zip" },
  "raw-bytes":    { "Codec": "base64" }
}
```

### 2.4 `MediaTypes` — extension → media type
Consumed by the `media-type` classifier stage. Optional; an unmapped extension is left alone.

---

## 3. Workflows — the FSE‑facing blueprint

A **Workflow** is the single thing an FSE names from a contract. It bundles the shared pipeline, the
default output encoding, and (optionally) a **`Settings`** form you design for the FSE.

```jsonc
"Workflows": {
  "adt": {
    "Pipeline": "main",           // optional: names a Pipelines entry. Omit = no processing stages.
    "Format":   "hl7-standard",   // the default per-leg encoding (see §3.1 for the multi-format form).
    "Version":  1,                // optional: bump when you change a locked value/default (fleet-wide lever).
    "Settings": { /* the delegated FSE form — see §4 */ }
  }
}
```

The FSE references it as an **object** (always):
```jsonc
// contractData.json
"Workflow": { "Use": "adt", "Settings": { "AckTimeoutSeconds": 45 } }
// zero-config form (no overrides):
"Workflow": { "Use": "adt" }
```

### 3.1 Single `Format` vs an ordered `Formats` set

You choose **one** of these two forms (declaring **both is an error**):

| Form | Meaning | Per‑output override |
|------|---------|---------------------|
| `"Format": "hl7-standard"` | Every output leg inherits this one format. | An output may override with **any** catalog `Format` (legacy escape hatch, unconstrained). |
| `"Formats": [ "hl7-standard", "raw-bytes" ]` | An **ordered menu**. Element **[0] is the default**. | Each output picks one **by name** (`Output.Format`), and it **must be a member of the set**. An output that omits it falls back to `Formats[0]` (logged). |

```jsonc
"adt-multi": { "Pipeline": "main", "Formats": [ "hl7-standard", "raw-bytes" ] }
```
Contract side, picking per leg:
```jsonc
"Outputs": [
  { "OutputId": 101 },                        // -> Formats[0] = hl7-standard (with a log note)
  { "OutputId": 102, "Format": "raw-bytes" }  // -> raw-bytes (must be a declared member)
]
```

---

## 4. `Settings` — the delegated FSE form

`Settings` is **the only place you delegate operational policy to the FSE**. Each entry is a friendly
name mapped to a hidden **`Bind`** target. The FSE fills a flat `key: value` bag; they never see field
paths, stage names, or modes.

> **Rule of thumb:** *if it's a Setting, the FSE can set it. If it's not a Setting, it's constant and
> invisible to them.* "Required" = a Setting with **no** `Default`. "Optional" = a Setting **with** a `Default`.

### 4.1 Anatomy of a Setting

```jsonc
"AckTimeoutSeconds": {
  "Description": "Seconds to wait for downstream delivery before NACK.", // shown in generated templates
  "Default":     "30",                        // string. Omit -> the Setting is REQUIRED.
  "Min":         5,                            // numeric lower bound (inclusive)
  "Max":         60,                           // numeric upper bound (inclusive)
  "Allowed":     [ "true", "false" ],          // exact allow-list (a "choice" setting)
  "Regex":       "^[A-Za-z0-9_-]+$",           // value must match this pattern
  "Bind":        "Acknowledgement.TimeoutMs",  // hidden target (see §4.2). Omit -> the key IS the field name.
  "Kind":        "file",                        // file | secret | (omit = a plain scalar). See §5 / §6.
  "ContentType": "application/json",           // for Kind:file only — recorded in the manifest.
  "Scale":       1000                           // multiply the numeric value before binding (seconds -> ms).
}
```

All fields except the effective target are optional. `Default` is always written as a **string**
(e.g. `"30"`, `"true"`) — the resolver converts it to the target's real type.

### 4.2 `Bind` — where the value goes

`Bind` is a dotted path. If omitted, the **key name itself** is the target field on the contract.

| Bind target | Example | Effect |
|-------------|---------|--------|
| A contract field | `Acknowledgement.TimeoutMs`, `Response.TimeoutMs`, `ReplyOnFilter` | Sets that field on the resolved contract. |
| A per‑output wildcard | `Outputs[].Retry.MaxAttempts`, `Outputs[].Channel.Capacity` | Sets the field on **every** output leg. |
| A per‑input wildcard | `Inputs[].Channel.Capacity` | Sets the field on **every** input. |
| A stage parameter | `stage:hl7-filter.Ruleset` | Handed to the named stage at construction (see §5). |

The value is converted to the target's type automatically: `string`, `bool`, enum
(case‑insensitive, e.g. `"Exponential"`), and numbers (`int`/`long`/`double`/`float`, with `Scale`
applied). A value that can't convert is a fail‑fast error.

### 4.3 `Scale` and guardrails

- **`Scale`** multiplies a numeric value before binding. The FSE thinks in seconds; the engine stores
  ms → `"Scale": 1000`.
- **Guardrails** are checked **before** binding, with plain‑language errors keyed by the friendly name:
  - `Allowed` — value must be one of the list.
  - `Min` / `Max` — numeric bounds (inclusive).
  - `Regex` — value must match (evaluated with a 1‑second timeout).

### 4.4 `ReplyOnFilter` is now a Setting

The old `ReplyOnFilter` field is expressed as an ordinary Setting that binds to the contract field:
```jsonc
"ReplyOnFilter": { "Default": "false", "Allowed": [ "true", "false" ], "Bind": "ReplyOnFilter" }
```

---

## 5. `Kind: file` — giving a stage a file (e.g. a ruleset)

Some stages need an **external file** (the classic case: an HL7 filter stage that reads a **ruleset**).
You own the stage; the FSE supplies (or accepts a default) the file. Model it as a `Kind: file` Setting
that **binds to a stage parameter**.

### Step 1 — declare a default file under `Resources`
`Resources` are developer‑shipped files. `Ref` is a path **relative to the resources root**; `ContentType`
is metadata recorded in the manifest.
```jsonc
"Resources": {
  "adt-default-rules": {
    "ContentType": "application/vnd.ibe.filter-rules+json",
    "Ref": "adt-default.rules.json"          // relative to the resources root (see Step 3)
  }
}
```

### Step 2 — expose it as a `Kind: file` Setting bound to the stage
```jsonc
"Workflows": {
  "adt": {
    "Pipeline": "adt-standard",              // a pipeline that includes the "hl7-filter" stage
    "Format":   "hl7-standard",
    "Settings": {
      "FilterRules": {
        "Description": "Ruleset file deciding which messages are dropped.",
        "Kind":        "file",
        "ContentType": "application/vnd.ibe.filter-rules+json",
        "Default":     "adt-default-rules",  // a Resources name (or a relative path the FSE supplies)
        "Bind":        "stage:hl7-filter.Ruleset"   // handed to the 'hl7-filter' stage as its 'Ruleset' param
      }
    }
  }
}
```

### Step 3 — where files live (the resources root)
File values resolve **inside a fixed allowed root: `<agent-exe-folder>/resources`**
(`AppContext.BaseDirectory/resources`). Put `adt-default.rules.json` in that folder. The FSE may instead
point the setting at **their own** relative path (e.g. `"FilterRules": "site-a/adt.rules.json"`) — it is
still resolved inside that same root.

### Security rules (enforced at resolution — untrusted FSE input)
1. The value is first looked up in `Resources`. If it matches a name, that entry's `Ref` (and its
   `ContentType`) are used. Otherwise the value is treated as a **relative path**.
2. **Absolute paths are rejected** (`C:\…`, `/etc/…`, UNC) — files must be relative to the root.
3. **Traversal is rejected** — a value that resolves outside the root (e.g. `../../secrets/x`) fails
   with *"escapes the allowed resources root."*
4. The resolved **absolute path** is what the stage receives, and it is recorded in the **manifest** (§7).

> Existence/checksum checks are a future refinement — today the resolver guarantees **confinement**, and
> the stage is responsible for reading the file. Also note: the `hl7-filter` stage itself ships later, so
> a `stage:hl7-filter.Ruleset` bind resolves and is threaded, but no shipped stage consumes it **yet**.

---

## 6. `Kind: secret` — passing a secret to a stage

A `Kind: secret` Setting resolves its value from a **secret store** (never inline in config, never in the
manifest). Declare it exactly like any other Setting, with `"Kind": "secret"`:
```jsonc
"ApiKey": {
  "Description": "Downstream API key.",
  "Kind":        "secret",
  "Default":     "downstream-api-key",       // the secret's NAME (looked up in the store), not the value
  "Bind":        "stage:hl7-enrich.ApiKey"
}
```
- The value you write (default or FSE‑supplied) is a **secret name**; the resolver looks it up and binds
  the **resolved value**.
- Secrets are **never** written to the resolved manifest or logs.

> **Status:** the secret **seam** is implemented, but the host does **not** wire a secret provider yet.
> A contract that uses a `Kind: secret` setting will therefore fail at startup with
> *"Setting 'X' is a secret but no secret resolver is configured."* Treat `Kind: secret` as forward‑looking
> until a secret store is wired into the host.

---

## 7. The resolved manifest

Every `Kind: file` that resolves is recorded in a per‑run **manifest** for ops discoverability. At startup
the host logs one line per resolved file at **Information** level:

```
Resolved resource <ContractName>/<SettingName> -> <absolute-path> (<ContentType>).
```

- **Files only.** `Kind: secret` values are **never** listed.
- It's an ops artifact answering *"which file is contract X actually using?"* — nothing is loaded from it.

---

## 8. Rules & validation (fail‑fast)

The engine rejects a bad catalog/contract at startup with a plain‑language message. Checklist:

**Catalog structure (validated whenever any contract compiles):**
- Every `Codecs` entry has a non‑empty `Type`.
- Every `Formats` entry's `Codec` (and `BatchCodec`, if present) resolves to a `Codecs` entry.
- Every `Pipelines` entry has ≥ 1 non‑blank stage name.
- Every `Workflows` entry's `Pipeline` (if set) resolves to a `Pipelines` entry.
- A Workflow declares **either `Format` or `Formats`, not both**.
- Every `Format`/`Formats[]` name on a Workflow resolves to a `Formats` entry (no blank entries).

**At resolution (when a contract uses the workflow):**
- `Workflow.Use` resolves to a `Workflows` entry.
- Every FSE‑supplied Setting is one the Workflow **exposed** (unknown settings are rejected).
- A Setting with **no `Default`** must be supplied by the FSE (else "required" error).
- Guardrails pass (`Allowed` / `Min` / `Max` / `Regex`); the value converts to the target type.
- A `Bind` path resolves to a real member; a `stage:` bind is `stage:<stage>.<param>`.
- `Kind: file` values obey the security rules (§5); `Kind: secret` needs a configured resolver (§6).
- With an explicit `Formats` set, each `Output.Format` is a **member** of the set.

**Cross‑checks:** every output's resolved `Encoding` resolves to a `Codecs` entry; if batching is enabled,
its resolved batch codec resolves too.

> Note: **Setting definitions are validated lazily** — a malformed `Bind` or an out‑of‑range default only
> surfaces when a contract actually uses that workflow. Exercise each workflow with a test contract.

---

## 9. Complete worked example

`catalogData.json` (developer):
```jsonc
{
  "Catalog": {
    "Codecs":    { "hl7v2": { "Type": "hl7v2" }, "base64": { "Type": "base64" } },
    "Pipelines": { "adt-standard": [ "hl7-classify", "hl7-filter" ] },
    "Formats":   { "hl7-standard": { "Codec": "hl7v2" }, "raw-bytes": { "Codec": "base64" } },

    "Resources": {
      "adt-default-rules": { "ContentType": "application/vnd.ibe.filter-rules+json", "Ref": "adt-default.rules.json" }
    },

    "Workflows": {
      "adt": {
        "Version":  1,
        "Pipeline": "adt-standard",
        "Formats":  [ "hl7-standard", "raw-bytes" ],     // multi-format menu; [0] is the default
        "Settings": {
          "AckTimeoutSeconds": { "Description": "Wait before NACK.", "Default": "30", "Min": 5, "Max": 60,
                                 "Bind": "Acknowledgement.TimeoutMs", "Scale": 1000 },
          "MaxRetries":        { "Description": "Delivery attempts.", "Default": "3", "Min": 1, "Max": 5,
                                 "Bind": "Outputs[].Retry.MaxAttempts" },
          "ReplyOnFilter":     { "Default": "false", "Allowed": [ "true", "false" ], "Bind": "ReplyOnFilter" },
          "FilterRules":       { "Description": "Ruleset file.", "Kind": "file",
                                 "ContentType": "application/vnd.ibe.filter-rules+json",
                                 "Default": "adt-default-rules", "Bind": "stage:hl7-filter.Ruleset" }
        }
      }
    }
  }
}
```

`contractData.json` (FSE) — tuned:
```jsonc
{
  "Contracts": [
    {
      "Name": "adt-fanout-siteA",
      "Workflow": {
        "Use": "adt",
        "Settings": {
          "AckTimeoutSeconds": 45,                 // 5..60 -> Acknowledgement.TimeoutMs = 45000
          "FilterRules": "site-a/adt.rules.json"   // FSE's own file (under the resources root)
          // MaxRetries + ReplyOnFilter omitted -> their defaults
        }
      },
      "Inputs":  [ { "InputId": 1 } ],
      "Outputs": [
        { "OutputId": 101 },                        // -> hl7-standard (Formats[0], logged)
        { "OutputId": 102, "Format": "raw-bytes" }  // -> raw-bytes (a declared member)
      ]
    }
  ]
}
```

Zero‑config equivalent (all defaults): `"Workflow": { "Use": "adt" }`.

---

## 10. Quick reference

**Setting fields:** `Description`, `Default`(string; omit ⇒ required), `Min`, `Max`, `Allowed`, `Regex`,
`Bind`, `Kind`(`file`|`secret`), `ContentType`(file only), `Scale`.

**Bind targets:** `Field.Sub` (contract) · `Outputs[].X` / `Inputs[].X` (wildcard) · `stage:<name>.<key>`.

**Kind:file:** value = a `Resources` name **or** a relative path under `<exe>/resources`; absolute &
traversal rejected; resolved path recorded in the manifest.

**Kind:secret:** value = a secret **name**; resolved from the store; never in the manifest (needs a host
secret provider, not yet wired).

---

## 11. Regenerating the copy‑paste templates

`config/templates/workflows/*.jsonc` are **generated** from `catalogData.json` so the FSE starters can't
drift from your Settings. After editing Workflows/Settings, regenerate them:

```pwsh
dotnet run --project tools/gen-templates -- "<repo-root>"   # path goes AFTER the --
```
