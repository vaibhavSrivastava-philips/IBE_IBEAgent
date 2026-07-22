# IBE Agent v3 Feature Gap and Parity Analysis

**Current-state baseline:** `docs/IBE_AGENT_FEATURE_INVENTORY.md`  
**Target design:** `Refactor_ArchitectureDoc_v3.md`  
**Analysis scope:** Functional parity between the checked-in IBE Agent product and the proposed v3 core parsing/data-processing architecture.

## 1. Executive Conclusion

The v3 document is a strong design for the new message-processing engine. It already covers the main architectural changes: many inputs to many outputs, contract runtimes, shared pipelines, parse-once processing, per-leg queues, codecs, batching, required/optional legs, acknowledgements, request-reply, durability, dead-letter handling, replay, backpressure, telemetry hooks, and deterministic shutdown.

It is not yet a complete product-replication specification. The largest omissions are not additional routing abstractions; they are the concrete compatibility requirements around those abstractions:

1. exact behavior of every current transport adapter;
2. live configuration reload and compatibility with the Web management control plane;
3. explicit compatibility contracts for the existing CIM clinical mapping and cloud-upload protocol that v3 delegates to existing components;
4. migration of failed-message data and operational requeue workflows;
5. product licensing, certificate, secret, authentication, and authorization integration;
6. Windows deployment, service management, observability compatibility, and PostgreSQL HA;
7. adjacent utilities and services that are outside the core engine but remain part of full product functionality.

The recommended architecture is therefore:

- retain v3 as the **core data-plane design**;
- add explicit **adapter parity specifications** for each transport and CIM output;
- define a **control-plane contract** for configuration, security, and monitoring;
- define an **operations and migration plan** for services, databases, telemetry, and HA;
- record explicit dispositions for partial and legacy capabilities.

Implementing only the classes and flows shown in v3 would reproduce the processing shape, but would not fully reproduce the current product.

## 2. Priority and Classification Model

### Priority

| Priority | Meaning for the proposed architecture |
|---|---|
| **P0 - Cutover blocker** | Required to process existing production flows correctly or to migrate without data loss, incompatible configuration, or broken source/downstream behavior. Must be designed and acceptance-tested before production cutover. |
| **P1 - Production parity** | Required for production security, administration, supportability, observability, or established user workflows. May be implemented outside the core libraries, but must exist before declaring full product parity. |
| **P2 - Supporting parity** | Packaging, administration, cloud provisioning, HA tooling, and test utilities needed for the complete supported product or operating model. Can follow the first core milestone if an equivalent retained component remains available. |
| **P3 - Disposition decision** | Legacy, partial, duplicated, or currently unshipped behavior. Do not automatically rebuild it; obtain an explicit retain, replace, complete, or retire decision. |

### Classification

| Classification | Meaning |
|---|---|
| **Core addition** | The v3 data-plane model itself needs an additional rule, contract, or lifecycle behavior. |
| **Adapter specification** | v3 has the extension point, but not enough protocol-specific behavior to guarantee current compatibility. |
| **Control-plane integration** | Management API/UI, configuration persistence, authentication, certificates, or operational workflows must integrate with the new runtime. |
| **Adjacent product service** | Required product capability that should remain separate from the core engine. |
| **Operations/tooling** | Build, install, service, telemetry, HA, diagnostics, or verification capability. |
| **Disposition** | No parity implementation should be assumed until product ownership confirms its future. |

## 3. What v3 Already Covers

The following are **not missing features**. They require implementation and tests, but the target document already represents them architecturally.

| Current or target capability | v3 coverage assessment |
|---|---|
| Many inputs to many outputs | Covered by ContractRuntime fan-out and per-output DeliveryLeg instances. |
| Per-contract ingress queue and per-leg queue | Covered by `IMessageChannel`, ContractRuntime, and DeliveryLeg. |
| Required and optional outputs | Covered by per-leg `Required` behavior and reply gating. |
| Backpressure and bounded queues | Covered, including required-leg coupling and overflow-policy discussion. |
| At-most-once and at-least-once delivery | Covered as a per-leg channel/durability choice. |
| Parse once, serialize per destination | Covered by `MessageContext.ParsedView`, the shared pipeline, and codecs. |
| HL7 filtering as a stage | Structurally covered by the pipeline/catalog and HL7 module. Existing filter-rule compatibility still needs migration and parity tests. |
| Per-message ACK ownership | Covered by `ReplyContext`, `IAckToken`, strategies, and formatters. |
| Original/enhanced ACK behavior | Substantively covered by the dedicated ACK/reply design. Exact current HL7 wire output remains an adapter acceptance requirement. |
| Request-reply | Covered as a new responder-leg mode. It is not a current-product parity gap. |
| Per-leg retry, DLQ, and targeted replay | Covered by DeliveryLeg, `IDeadLetterQueue`, and `DlqRetryWorker`. Existing data migration remains a gap. |
| Per-leg batching | Covered through `IBatchCodec` and the batching outbound decorator. |
| TCP, HTTP, WebSocket, File endpoints | Named in the project structure and endpoint abstractions. Their detailed current behavior is underspecified. |
| TCP, HTTP, File, and CIM/S3 output concepts | Represented in the endpoint/project examples. CIM's complete business protocol is not specified. |
| OpenTelemetry hooks and queue/stage/leg visibility | Covered architecturally. Existing metric and log compatibility is not specified. |
| Graceful shutdown and drain ordering | Explicitly covered, including parking uncommitted messages and flushing batches. |
| Catalog, component registry, compiler, and validation | Covered as the target configuration architecture. Schema migration and live reload are not covered. |
| Windows Service host | Included in the proposed project structure. Installation and service lifecycle workflows are not covered. |

## 4. P0 - Cutover Blockers

### GAP-P0-01: Define exact inbound transport compatibility

**Classification:** Adapter specification  
**v3 status:** Transport types are named, but their complete wire and lifecycle contracts are not specified.

The new endpoints must preserve these current behaviors:

- TCP/MLLP framing and de-framing, connection lifecycle, secure mode, source-connection ACK writing, and malformed/incomplete frame handling;
- the separately configurable ADT TCP listener and its independent runtime switch;
- HTTP and HTTPS listener paths, accepted content behavior, response status/body semantics, certificate behavior, and overload response behavior;
- WebSocket connectivity, reconnect/cancellation behavior, proxy support, protected proxy credentials, and reply ownership;
- independent enable/disable switches for TCP, ADT, HTTP, WebSocket, and File nodes.

**Required architecture addition:** Add an endpoint compatibility specification per protocol. Each specification should define framing, TLS/certificate/proxy options, admission control, timeout behavior, cancellation, reply semantics, and configuration fields. Treat ADT as an explicit endpoint profile or adapter, not an undocumented alias for generic TCP.

**Acceptance evidence:** Golden-wire tests using existing endpoint configurations and the TCP/MLLP harness; HTTP and WebSocket integration tests for success, rejection, timeout, disconnect, TLS, and proxy cases.

### GAP-P0-02: Preserve the complete file-ingestion lifecycle

**Classification:** Adapter specification  
**v3 status:** File is named as an endpoint, and a file move is mentioned as a possible reply action, but current ingestion semantics are not defined.

Parity requires:

- polling configured local or network locations;
- waiting for network availability where applicable;
- deterministic file filtering and ordering;
- multi-file/batch parsing behavior;
- prevention of duplicate processing across polling cycles;
- movement of handled files into a `Processed` location;
- processed-file retention and deletion after configured retention days;
- safe startup/restart behavior and cancellation during file processing.

**Required architecture addition:** Define a File Inbound Adapter profile plus a retention/background-maintenance service. Specify when a file is claimed, when it is considered accepted, how partial processing is recovered, and when it is moved or retained.

**Acceptance evidence:** Restart, locked-file, network-loss, duplicate-poll, ordered-processing, processed-move, and retention-expiry tests.

### GAP-P0-03: Add live configuration reload and atomic runtime replacement

**Classification:** Core addition and control-plane integration  
**v3 status:** Catalog loading and contract compilation are described, primarily as startup construction. The current dynamic cache and Web-to-Agent update lifecycle are not specified.

Current behavior watches JSON configuration changes so Web-managed communication points and contracts can be consumed without rebuilding the application. The target must define:

- which configuration changes are hot-reloadable;
- validation before activation;
- atomic publication of a new compiled registry;
- behavior of in-flight messages on the old runtime;
- start/stop ordering for added, removed, or changed endpoints and legs;
- rollback when compilation or endpoint startup fails;
- file-write coordination so partially written JSON is never loaded;
- version/concurrency handling between multiple Web edits.

**Required architecture addition:** Introduce a configuration snapshot/version model and a runtime-reconfiguration coordinator. Existing messages should complete against the immutable snapshot selected at dispatch; new messages should use the newly activated registry.

**Acceptance evidence:** Concurrent edit/load tests, invalid-change rollback, endpoint replacement, contract removal with in-flight messages, and no-loss/no-double-send tests during reload.

### GAP-P0-04: Define configuration compatibility and migration

**Classification:** Control-plane integration  
**v3 status:** A new contract/catalog schema is proposed, but migration from the current files and APIs is not specified.

The current product persists communication points, contracts, node settings, ACK/high-fidelity options, credentials, and related settings in JSON. v3 changes the contract shape from one output to `Outputs[]` and adds pipelines, codecs, channels, guarantees, batching, and response policy. V3 already provides a useful backward-compatible shorthand: a current `OutputId` can compile to one required output and an omitted pipeline can compile to no stages. Migration work should build on that rule rather than redesign the single-output case.

**Required architecture addition:** Provide:

- a versioned configuration schema;
- a deterministic old-to-v3 migration tool or compatibility reader;
- mapping rules for current ACK, high-fidelity, retry, filter-file, node-switch, proxy, TLS, file, and S3 fields;
- explicit conversion of the active `filterHL7.json` rule schema and each contract's filter-file reference into the catalog/HL7-stage representation;
- defaults that preserve current single-output behavior;
- validation and a human-readable migration report;
- rollback or backup of the pre-migration configuration.

**Acceptance evidence:** Migrate every checked-in configuration sample, compile it, and compare the resulting endpoint/contract topology and policy values with the current runtime.

### GAP-P0-05: Specify complete HL7 behavior, not only a generic HL7 stage

**Classification:** Adapter/module specification  
**v3 status:** HL7 parsing, filtering, ACK formatting, and MLLP are represented, but current rule and wire compatibility are not fully defined.

Parity requires:

- existing segment/field and message-type filtering rules;
- handling of filtered messages and the corresponding ACK outcome;
- original versus enhanced acknowledgment timing;
- positive and negative ACK formatting and source control-ID correlation;
- inbound/downstream ACK parsing and success/failure classification;
- high-fidelity and ACK mutual-exclusion rules where retained for migrated contracts;
- preservation of current encoding, delimiters, control characters, and MLLP framing.

**Required architecture addition:** Add a versioned HL7 module contract containing parser behavior, filter-rule schema, ACK decision table, formatter golden samples, and migrated high-fidelity semantics.

**Acceptance evidence:** Golden-message tests over representative ADT and device messages, including filtered, malformed, timeout, downstream NACK, enhanced ACK, and retransmission cases.

### GAP-P0-06: Make the production CIM mapping contract explicit

**Classification:** Adapter/module specification  
**v3 status:** An S3 leg and Avro ZIP batch codec are examples, but the clinical transformation is not specified at production fidelity.

The active CIM pipeline includes:

- patient identity/demographic and patient-time mapping;
- numeric/vital measurement mapping;
- waveform/signal mapping;
- alert/alarm mapping, including announcement timing;
- source-device metadata;
- serialization against `PayloadSchema.avsc`;
- record-size calculation and chunking;
- multi-file parsing;
- category and batch metrics.

**Required architecture addition:** Treat this as a first-class `CimHl7ToAvro` module with a versioned canonical mapping specification. Define required HL7 fields, defaults, identifiers, timestamps, units, chunk boundaries, schema evolution, invalid-record behavior, and metric outcomes. Avoid reducing it to a generic `AvroZipBatchCodec`; mapping is business logic, while ZIP encoding and upload are delivery concerns.

**Acceptance evidence:** Golden Avro records for every mapping category, schema compatibility tests, boundary-size chunk tests, and byte/semantic comparison with current representative output.

### GAP-P0-07: Specify the complete CIM cloud-delivery transaction

**Classification:** Adapter specification  
**v3 status:** S3 batching/upload is represented, but the current multi-step cloud protocol is not.

Parity requires:

- ZIP staging and ordered deferred flush when `UploadHoldSeconds > 0`;
- certificate/JWT/token authentication and HSDP request signing;
- presigned URL acquisition and S3-compatible HTTP upload;
- upload retry and proxy/TLS behavior;
- folder UUID/batch identifier rotation at the correct commit point;
- RabbitMQ completion notification after successful upload;
- failure handling when upload succeeds but notification fails;
- no duplicate notification or incorrect UUID rotation during retry/restart.

**Required architecture addition:** Model CIM delivery as a stateful outbound workflow or transactional endpoint with explicit states such as `Staged`, `Authorized`, `Uploaded`, `Notified`, and `Committed`. Define idempotency keys and recovery for every transition. A generic endpoint `SendAsync` result is insufficient unless it persists this state.

**Acceptance evidence:** Failure-injection tests at token, presigned URL, upload, notification, rotation, and restart boundaries.

### GAP-P0-08: Migrate failed-message persistence and replay safely

**Classification:** Core migration and control-plane integration  
**v3 status:** The target per-leg DLQ and direct replay behavior are covered. Existing schema compatibility, service coexistence, and operator workflows are not.

Parity and migration require:

- migration of existing PostgreSQL failure records to the new message/leg identity model;
- preservation of sender/communication-point information and payload encoding;
- a decision on PostgreSQL versus SQLite ownership for failure and Web transaction views;
- coexistence or cutover rules for the current Forward Service;
- sender-specific replay for CIM S3, HTTP, and TCP during transition;
- successful-record cleanup;
- no rerouting, second ACK, or duplicate delivery to already successful legs;
- handling of records whose old communication point no longer exists.

**Required architecture addition:** Add a failure-store schema migration, compatibility reader, cutover sequence, and reconciliation report. Define whether the Forward Service is retained as the `DlqRetryWorker` host or replaced by an in-process worker.

**Acceptance evidence:** Restore a copy of the current failure store, migrate it, replay representative records, and prove target-leg-only delivery and cleanup.

If the Forward Service remains a separate host during or after migration, preserve its independently configured proxy-aware HTTP retries and optional OTLP logging. If it is replaced, map those settings into the new DLQ worker host and document the service/configuration transition.

### GAP-P0-09: Define behavioral parity and cutover gates

**Classification:** Operations/testing  
**v3 status:** The phased strategy is described, but the current product's complete parity suite and release gates are not.

Required gates include:

- golden input/output tests for every active input/output pair;
- original/enhanced ACK and filtered-message timing tests;
- CIM mapping and cloud-workflow tests;
- dynamic reload tests;
- failed-record migration and replay tests;
- graceful shutdown with in-flight single and batched messages;
- bounded-load/backpressure tests and current throughput baselines;
- TLS, proxy, certificate, and protected-secret tests;
- old/new shadow comparison with correlation IDs and outcome reconciliation.

**Required architecture addition:** Add a parity test matrix to the migration plan. A phase is complete only when its relevant matrix rows pass, not merely when the proposed interfaces exist.

The old-to-new metric name, label, and correlation-field mapping is a P0 prerequisite for shadow comparison and outcome reconciliation. Broader logging and exporter compatibility remains P1.

### GAP-P0-10: Enforce safe database-disabled operation

**Classification:** Core/configuration integration  
**v3 status:** Partially covered by the explicit `NullDatabaseUtils`/Null Object pattern; valid configuration combinations and UI/runtime restrictions are not defined.

The compiler must define which guarantees and features are available when persistence is disabled. Migrated database-disabled installations must continue routing without an accidental PostgreSQL dependency, while rejecting durable channels, `AtLeastOnce` legs, or DLQ-dependent policies that cannot be honored. The Web Error Queue feature must remain disabled or clearly unavailable in this mode.

**Acceptance evidence:** Compile and run representative database-disabled configurations; verify in-memory delivery still works and every persistence-dependent configuration fails fast with an actionable validation error.

## 5. P1 - Production Parity

### GAP-P1-01: Preserve Web management API and Angular workflows

**Classification:** Control-plane integration  
**v3 status:** The target document defines configuration DTOs/compiler behavior but not the current management product.

Full parity requires authenticated UI/API workflows for:

- communication-point list/get/create/update/delete;
- contract list/create/update/delete;
- service-node settings for HTTP, TCP, WebSocket, and ADT;
- ACK, high-fidelity, input/output, retry, proxy, TLS, file, and CIM settings;
- protected-value sanitization in responses;
- validation feedback, confirmation dialogs, and delete warnings;
- static Angular hosting, development CORS, and Development-only Swagger/OpenAPI.

The UI and API must be redesigned for `Outputs[]`, required/optional legs, pipeline/codec selection, durability, queue policy, batching, and response policy without losing existing workflows.

### GAP-P1-02: Preserve the Error Queue and manual requeue workflow

**Classification:** Control-plane integration  
**v3 status:** Automatic per-leg DLQ replay is covered; the operator-facing transaction workflow is not.

Required behavior includes listing failed transactions, retrieving sufficient failure context, updating/requeueing by ID, deleting or resolving successful records, enforcing the `DatabaseEnabled` feature gate, and showing leg/output status in the new model. Manual replay must target only the failed leg and must not generate another source ACK.

### GAP-P1-03: Preserve heartbeat and service-node monitoring

**Classification:** Control-plane integration  
**v3 status:** Queue and processing telemetry are covered; current server/client heartbeat views and endpoint availability checks are not.

Define health contracts for the Agent host, Forward/DLQ worker, Web server, configured endpoints, database, and local telemetry collector. Preserve the existing server heartbeat, client heartbeat, and authenticated UI views or provide a documented replacement.

### GAP-P1-04: Integrate product-license enforcement

**Classification:** Control-plane/security integration  
**v3 status:** Not represented in the core architecture.

Parity requires signed license signature/expiry validation for Agent and Web startup, configured license-path handling, and `--validate-license <path>` with installer-compatible exit codes and installation-window behavior. License validation must occur before endpoints accept data.

### GAP-P1-05: Preserve authentication, authorization, and logout behavior

**Classification:** Control-plane/security integration  
**v3 status:** Not represented.

Required behavior includes login and signed JWT issuance, issuer/audience/lifetime/signing-key validation, AD/group-to-role mapping, Admin and Normal route/API authorization, token attachment by the Angular client, logout invalidation, and expired-token cleanup. The current in-memory blacklist may be retained for single-node parity or deliberately replaced by persistent/distributed revocation for HA.

### GAP-P1-06: Preserve certificate, TLS, proxy, and secret management

**Classification:** Cross-cutting security integration  
**v3 status:** Protocol projects exist, but secure configuration ownership and management are not defined.

Required behavior includes:

- TLS 1.2 minimum for the Web server;
- per-endpoint X.509/TLS settings;
- admin-only single/multiple certificate upload and certificate deletion;
- protected proxy credentials and enterprise proxy behavior;
- DPAPI LocalMachine protection of password fields and runtime decryption;
- sanitization so APIs do not return protected secret values;
- secure PostgreSQL credentials.

Define a secret-provider abstraction and certificate reference model so the new catalog/compiler resolves references without embedding plaintext in compiled runtime objects or logs.

### GAP-P1-07: Maintain observability compatibility

**Classification:** Operations/tooling  
**v3 status:** Rich generic telemetry is covered, but existing operators and metric consumers are not.

Preserve or intentionally map:

- NLog file/console logging and rotation;
- optional OTLP logs for Agent and Forward behavior;
- optional OTLP metrics and runtime instrumentation;
- received, processed, sent, filtered, failed, ACK success/failure, and message-type counters;
- CIM record-category and batch metrics;
- per-minute conversion, upload, end-to-end timing, and throughput reports;
- existing correlation fields needed to compare old and new paths.

Create an old-to-new metric name/label table and control metric cardinality. Make OTLP endpoint, protocol, and export interval configurable rather than preserving the current hard-coded local URL.

### GAP-P1-08: Preserve Windows service and installer behavior

**Classification:** Operations/tooling  
**v3 status:** Windows Service hosting is named; release packaging and installation behavior are not.

Parity requires publish/package outputs, administrative privilege checks, install/reinstall/uninstall of Agent, Forward, and Web services, automatic startup, license-gated rollback, license-path injection, service start timeout/logging, and optional OpenTelemetry Collector installation. Service names and upgrade behavior must be compatible or covered by a migration script.

## 6. P2 - Supporting and Adjacent Product Parity

### GAP-P2-01: Retain CIM tenant onboarding

**Classification:** Adjacent product service

The onboarding utility is outside the parsing engine but part of the current deliverable. Preserve environment selection, IAM/IDM authentication, application/proposition provisioning, organization/group setup, service-key management, certificate creation, and result/error logging, or identify the replacement provisioning workflow.

### GAP-P2-02: Retain the Cloud License Updater

**Classification:** Adjacent product service

Preserve environment selection, tenant/service/collector/institution metadata collection, JWT/access-token exchange, Clinical Insights Gateway license update, cancellation, and stage-level error reporting, or document its replacement.

### GAP-P2-03: Preserve PostgreSQL HA operations

**Classification:** Operations/tooling

The core design does not cover standby promotion, old-primary rewind, replication restoration, failover logging, or registry-based secret propagation into Agent/Forward configuration. Retain and validate the current scripts or replace them with an equivalent HA operating model. Externalize PostgreSQL version, path, host, IP, account, and service assumptions.

### GAP-P2-04: Preserve license administration utilities

**Classification:** Operations/tooling

Full product operations currently include Base64 license-request collection, development/production signed-license generation, certificate-store signing by thumbprint, and DPAPI protection for local licensing data. These can remain separate from the runtime, but need packaging and ownership.

### GAP-P2-05: Preserve certificate and password setup utilities

**Classification:** Operations/tooling

Retain or replace self-signed CA/server/client certificate generation and recursive DPAPI encryption of password-named JSON fields. Update these tools for the versioned v3 schema and certificate-reference model.

### GAP-P2-06: Preserve diagnostic and integration-test assets

**Classification:** Operations/testing

Retain the TCP/MLLP harness's client, server, combined, load, ACK, and round-trip timing modes. Update the CIM Postman collection and add v3 contract/catalog examples. Preserve relevant Web controller, JWT invalidation, service/DB, and Angular tests while adding core-engine tests.

### GAP-P2-07: Resolve Metrics Viewer packaging

**Classification:** Operations/tooling

The metrics viewer exists but is not copied by the current build. Decide whether archived `ibe_metrics` JSON remains a supported compatibility output. If yes, package and test the viewer; if OTel dashboards replace it, document migration and retire it explicitly.

## 7. P3 - Explicit Disposition Required

| Item | Current state | Required decision |
|---|---|---|
| Standalone HL7-to-Avro Converter | Published, but functional hosted-service registrations are commented out; active conversion is in CIM.Common. | Do not rebuild by default. Either restore a tested standalone entry path with a clear use case or stop packaging it. |
| Cart Gateway configuration | Model and validation exist, but no current dispatch/output implementation was found. | Not required for current functional parity. Complete only with a confirmed product requirement; otherwise remove from migrated schema/UI. |
| Legacy Service.Agent | Superseded implementation, absent from the current build/install path. | Do not port wholesale. Confirm field deployment dependencies before archive/removal. |
| Legacy ReportSync | Functionally distinct WebSocket-to-TCP report relay with ACK/retry behavior inside the legacy service. | Confirm contractual/deployed use. If active anywhere, specify it as a separate adapter/workflow; otherwise retire explicitly. |
| Duplicate converter/mappers | Parser/mapper code exists in both the converter project and CIM.Common. | Consolidate on one production mapping module after deciding converter ownership. |
| Duplicate certificate script and nested solution | No independent current runtime role established. | Remove or retain only after build/release-owner confirmation. |

## 8. Architecture Additions Recommended for v3

The following additions would turn the engine blueprint into a complete replication blueprint without polluting the core with UI or installer concerns.

1. **Protocol Compatibility Annex**
   - TCP/MLLP, ADT TCP, HTTP/HTTPS, WebSocket, File, TCP output, HTTP output, File output, and CIM output behavior.
   - Wire formats, timeouts, retries, TLS, proxy, certificates, replies, overload, cancellation, and shutdown.

2. **CIM Mapping and Delivery Annex**
   - Clinical mapping rules, Avro schema/versioning, chunking, ZIP batches, authentication, presigned upload, RabbitMQ notification, UUID rotation, and recovery state machine.

3. **Configuration and Runtime Reload Design**
   - Versioned schemas, migration, snapshots, atomic activation, endpoint replacement, rollback, and Web edit concurrency.

4. **Control-Plane Contract**
   - Management APIs/UI, authorization, certificate/secret handling, error queue, requeue, heartbeat, and feature switches.

5. **Persistence and Cutover Design**
   - Failure-store schema, migration, old Forward Service coexistence, target-leg replay, database-disabled mode, reconciliation, and rollback.

6. **Operations and Security Design**
   - Licensing, Windows services, installer/upgrade, OTLP Collector, NLog/metric compatibility, PostgreSQL HA, certificate/password utilities, and service identities.

7. **Parity Verification Plan**
   - Golden messages, wire tests, failure injection, migration tests, shadow comparison, load/backpressure, security, and operator workflow tests.

## 9. Inventory-to-v3 Coverage Matrix

| Current inventory area | v3 status | Remaining action | Highest priority |
|---|---|---|---|
| Startup, hosting, licensing | Partial | Windows host covered; add license startup/CLI and installer contract. | P1 |
| TCP/MLLP input | Partial | Specify exact framing, security, connection, ACK, timeout, and overload behavior. | P0 |
| ADT TCP input | Missing/implicit | Define independent endpoint profile and configuration migration. | P0 |
| HTTP/HTTPS input | Partial | Specify routes, request/response, TLS/certificate, timeout, and overload semantics. | P0 |
| WebSocket input | Partial | Specify connection/reconnect, proxy, security, cancellation, and reply semantics. | P0 |
| File input and retention | Partial | Specify polling, ordering, claim/restart, Processed move, network handling, and retention. | P0 |
| Contract routing and queues | Covered | Implement and verify v3 ContractRuntime/DeliveryLeg design. | Covered |
| HL7 filtering and ACK/NACK | Partial | Add exact rule-schema and wire-compatible behavior. | P0 |
| High-fidelity behavior | Covered | V3 design is complete; map the old flag to delivery, batching, and ACK policies and add golden-config tests. | P0 migration gate |
| Dynamic configuration cache | Missing | Add snapshot, compile, activation, rollback, and in-flight semantics. | P0 |
| TCP/HTTP/File outputs | Partial | Add adapter-specific retries, response, proxy/TLS, persistence, and wire acceptance criteria. | P0 |
| CIM S3 output/deferred upload | Partial | Specify mapping and transactional cloud protocol. | P0 |
| PostgreSQL failure storage | Partial | Migrate schema/data and define cutover/coexistence. | P0 |
| Database-disabled mode | Partial | Build on v3's Null Object pattern; define and validate allowable guarantees and feature behavior. | P0 |
| CIM clinical mappings | Missing | Add versioned mapping specification and golden tests. | P0 |
| Cloud upload/RabbitMQ workflow | Partial | Add auth, presign, upload, notify, UUID, idempotency, and recovery states. | P0 |
| Forward Service | Partial | Decide host ownership and migration/coexistence; preserve sender-specific retries. | P0 |
| Onboarding | Outside core | Retain as adjacent utility or provide replacement. | P2 |
| Cloud License Updater | Outside core | Retain as adjacent utility or provide replacement. | P2 |
| Web authentication/authorization | Missing | Integrate JWT, AD groups/roles, logout invalidation, and license gate. | P1 |
| Configuration API/UI | Missing | Redesign for v3 schema while preserving workflows and secret sanitization. | P1 |
| Error Queue/manual requeue | Missing | Connect UI/API to per-leg DLQ safely. | P1 |
| Heartbeat/monitoring UI | Missing | Define health contracts and preserve views/workflows. | P1 |
| Certificate/secret management | Missing | Add secret provider, certificate references, upload/delete, DPAPI, and sanitization. | P1 |
| NLog and existing metrics | Partial | Add compatibility map and configurable exporters. | P1 |
| Build/package/Windows installation | Partial | Host covered; preserve package, service, license rollback, logs, and collector installation. | P1 |
| License administration | Outside core | Retain and package separately. | P2 |
| PostgreSQL HA | Outside core | Retain or replace with equivalent operating model. | P2 |
| Unit/frontend tests and TCP harness | Outside core | Retain and extend into parity suite. | P2/P0 gates |
| Cart Gateway configuration | Intentionally incomplete | Obtain a complete-or-remove decision; it is not required for current runtime parity. | P3 |
| Partial/legacy/removal candidates | Intentionally excluded | Obtain explicit dispositions; do not silently port. | P3 |

## 10. Final Review and Completeness Check

This analysis was reviewed against every major section of the current feature inventory and against the complete v3 document.

### Review results

- **Core engine:** v3's routing, queue, pipeline, codec, ACK/reply, retry, DLQ, backpressure, ordering, batching, telemetry-hook, and shutdown concepts were not incorrectly reported as absent.
- **Inputs and outputs:** all active input types and output types were checked. Generic endpoint presence was distinguished from concrete behavioral parity.
- **CIM:** transformation categories, Avro/schema behavior, chunking, deferred ZIP upload, authentication, presigned upload, folder UUID rotation, RabbitMQ notification, and replay were included.
- **Configuration/control plane:** dynamic reload, schema migration, Web CRUD, service nodes, certificates, protected secrets, Error Queue, heartbeat, feature switches, and SPA/API hosting were included.
- **Security/licensing:** TLS, X.509, proxy credentials, DPAPI, JWT, AD roles, blacklist behavior, product license startup checks, installer validation, and license tools were included.
- **Operations:** Windows services, installer behavior, OTLP Collector, NLog/metrics compatibility, PostgreSQL HA, packaging, and diagnostics were included.
- **Adjacent services:** onboarding and Cloud License Updater were included as retained adjacent capabilities rather than core-engine responsibilities.
- **Tests:** existing Web/frontend tests, TCP harness, Postman assets, and the required new parity gates were included.
- **Legacy/partial items:** converter, Cart Gateway, Service.Agent, ReportSync, and duplicate artifacts were separated from mandatory parity and assigned disposition decisions.

### Known product-owner confirmations still required

Source analysis cannot determine contractual deployment obligations. Before final scope approval, confirm:

1. whether any supported deployment still uses legacy ReportSync or the old Service.Agent;
2. whether the standalone converter is meant to become operational or should leave the package;
3. whether Cart Gateway is a committed future feature or stale configuration surface;
4. whether Metrics Viewer and archived JSON metrics remain supported;
5. whether the existing Web API routes and JSON files are public compatibility contracts;
6. whether PostgreSQL HA scripts are a shipped support obligation or site-specific tooling;
7. required throughput, latency, retention, and recovery-point/recovery-time targets for parity gates.

Subject to those product-owner decisions, this document accounts for all active, supporting, partial, and legacy feature groups in the current inventory and identifies where each belongs relative to the v3 architecture.