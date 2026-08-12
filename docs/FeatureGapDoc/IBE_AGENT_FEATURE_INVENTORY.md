# IBE Agent Feature Inventory

**Repository:** `IBE_IBEAgent`  
**Inventory date:** 2026-07-20  
**Scope:** All source projects, runtime services, libraries, UI workflows, configuration, deployment scripts, operations tooling, tests, and identifiable legacy or incomplete capabilities in this workspace.

## 1. Purpose and Method

This document inventories features implemented in the repository and separates current product behavior from supporting tools, incomplete code, and legacy implementations. It is based on:

- all solution and project manifests under `src/`;
- application entry points and dependency registrations;
- controllers, Angular routes, services, node/sender implementations, and shared models;
- build, installation, security, metrics, and PostgreSQL HA scripts;
- configuration and schema files;
- unit tests, frontend tests, the TCP harness, and the Postman collection;
- GitNexus analysis of 5,367 symbols, 13,392 relationships, and 268 indexed execution flows.

Generated `bin/`, `obj/`, coverage, and test-result artifacts were not treated as independent features. Their presence was used only as secondary evidence that code had been built or exercised.

### Lifecycle labels

| Label | Meaning |
|---|---|
| **Active - critical** | Registered, built, or installed production behavior central to data flow, security, or availability. |
| **Active** | Registered or directly used production behavior. |
| **Supporting** | Administration, setup, diagnostics, testing, or shared behavior needed around the product. |
| **Partial** | Built or modeled, but its runtime path is disabled or incomplete. |
| **Legacy** | Superseded implementation retained in source and excluded from the current build/install path. |
| **Candidate for removal** | Duplicate or orphaned artifact with no current build/runtime role found. Confirm with product and release owners before deletion. |

Importance and lifecycle are engineering assessments from repository evidence, not declarations of product support policy.

## 2. Executive Summary

IBE Agent is a Windows-hosted healthcare integration platform. Its primary role is to ingest HL7-oriented data through TCP/MLLP, ADT TCP, HTTP, WebSocket, or files; apply contract-driven routing and filtering; and deliver data through TCP, HTTP, files, or a CIM cloud pipeline that converts HL7 to Avro and uploads ZIP batches through presigned S3 URLs. Failed delivery can be persisted in PostgreSQL and replayed by a separate Forward Service.

The current product surface also includes an Angular management application and ASP.NET Core API for configuration, certificates, authentication, error-queue handling, and heartbeat monitoring. Supporting utilities cover CIM tenant onboarding, cloud-license updates, local license generation, certificate creation, DPAPI secret protection, Windows service installation, telemetry, and PostgreSQL failover.

The current build targets .NET 10 and Angular 21, despite the root README still describing .NET 8 and Angular 18. The build publishes six applications plus the Angular client. The installer installs three persistent Windows services: Agent, Forward, and Web.

### Portfolio at a glance

| Category | Count | Current assessment |
|---|---:|---|
| C# projects | 12 | 8 current runtime/library projects, 2 tools, 1 test project, 1 legacy service |
| Angular applications | 1 | Active management UI |
| Current build outputs | 6 | Agent, Forward, Web, Onboarding, Cloud License Updater, Avro Converter |
| Installed Windows services | 3 | Agent, Forward, Web |
| Shared libraries | 2 | Agent.Common and CIM.Common |
| Primary persistence | 2 | PostgreSQL for failed data; SQLite for Web transaction/error data |
| Main external systems | 5+ | Philips IAM/IDM/HSDP, Clinical Insights Gateway, S3-compatible upload, RabbitMQ notification, OpenTelemetry Collector |

## 3. Project and Deliverable Matrix

| Project/deliverable | Type | Role | Lifecycle | Importance | Evidence |
|---|---|---|---|---|---|
| `Philips.IBE.IBEAgent.Service` | .NET worker/Windows service | Primary routing and transformation engine | Active - critical | Critical | `Program.cs`, `Nodes/`, `Workflow/`, `build.bat`, installer |
| `Philips.IBE.IBEAgent.Common` | .NET library | Shared contracts, configuration, database, filtering, ACK, caching, retention | Active - critical | Critical | Referenced by Agent and Forward |
| `Philips.IBE.IBEAgent.CIM.Common` | .NET library | HL7-to-Avro mapping, batching, ZIP staging, cloud upload, CIM metrics | Active - critical | Critical | Used by Agent CIM path and Forward |
| `Philips.IBE.IBEAgent.ForwardService` | .NET worker/Windows service | Replays persisted failed deliveries | Active - critical | Critical | Hosted service registration, build, installer |
| `Philips.IBE.Service.WebAgent.Server` | ASP.NET Core/Windows service | Management API and Angular static host | Active - critical | Critical | Controllers, `Program.cs`, build, installer |
| `philips.ibe.service.webagent.client` | Angular SPA | Administration and monitoring UI | Active | High | Angular routes, services, production build |
| `Philips.IBE.CIM.OnboardingService` | Console utility | Provisions CIM/HSDP tenant resources | Supporting | Medium | Built by `build.bat`; onboarding service layer |
| `Philips.IBE.CIM.CloudLicenseUpdater` | Interactive console utility | Updates tenant/clinical-unit license information in cloud | Supporting | Medium | Built by `build.bat`; IAM and CI API clients |
| `Philips.IBE.HL7toAvroConverter.V1.Service` | .NET host | Standalone conversion service scaffold | Partial | Low until completed | Built, but listener/processor registrations are commented out |
| `Philips.IBE.IBEAgent.TcpTestHarness` | Console test utility | End-to-end TCP/MLLP and ACK testing | Supporting | Medium | Client/server/both modes and HL7 factory |
| `Philips.IBE.Service.WebAgent.Server.UnitTest` | xUnit project | Web API, auth, middleware, service, and DB tests | Supporting | High | Controller and JWT invalidation tests |
| `Philips.IBE.IBEAgent.Licensing` | Interactive console utility | Generates signed development/production licenses | Supporting | High | Separate solution and license service |
| `Philips.IBE.Service.Agent/IBEAgent` | Older worker service | Previous CloudBridge/ReportSync router | Legacy | Medium while deployments remain | Separate solution; not published or installed by current scripts |

## 4. Core Agent Features

### 4.1 Startup, hosting, and licensing

| Feature | Behavior | Lifecycle | Importance |
|---|---|---|---|
| Windows Service hosting | Runs the primary Agent through `Microsoft.Extensions.Hosting` and `AddWindowsService`. | Active - critical | Critical |
| Signed license enforcement | Validates the configured `Philips.IBE.Agent` license before normal startup and rejects invalid or expired licenses. | Active - critical | Critical |
| Installer validation mode | `--validate-license <path>` validates installation window and signature, returns a process exit code, and is used by the installer before starting the service. | Active - critical | Critical |
| Configuration binding | Binds nodes, workflow, communication points, contracts, database settings, and license from JSON configuration. | Active - critical | Critical |
| Startup validation | Rejects invalid/missing contract references, duplicate or malformed communication points, missing protocol configuration, and incompatible routing options. | Active - critical | Critical |
| Contract-scoped node loading | Loads only communication points referenced as contract inputs or outputs. | Active | High |

### 4.2 Input channels

| Feature | Behavior | Lifecycle | Importance |
|---|---|---|---|
| TCP/MLLP listener | Receives framed HL7 messages over TCP, supports secure configuration, queues data for contract processing, and can return ACKs on the source connection. | Active - critical | Critical |
| ADT TCP listener | Separately configurable TCP listener for ADT traffic. | Active | High |
| HTTP/HTTPS listener | Receives messages through HTTP and HTTPS endpoints. | Active - critical | Critical |
| WebSocket input | Maintains WebSocket input connectivity, including optional proxy support. | Active | High |
| File ingestion | Polls configured file locations, processes file-based batches, and moves handled files into a `Processed` location. | Active | High |
| Runtime node switches | Each TCP, ADT, HTTP, WebSocket, and File hosted service is enabled independently through configuration. | Active | High |

### 4.3 Contract-driven routing and processing

| Feature | Behavior | Lifecycle | Importance |
|---|---|---|---|
| Communication points | JSON-defined inputs/outputs encapsulate protocol, address, TLS, proxy, retry, S3, file, and related options. | Active - critical | Critical |
| Routing contracts | Connect one or more input IDs to an output ID and define ACK, filtering, and high-fidelity behavior. | Active - critical | Critical |
| In-memory producer/consumer queues | Uses channels/queues to decouple input receipt from output delivery and support concurrent processing. | Active - critical | Critical |
| HL7 filtering | Applies segment/field rules, including message-type-oriented filtering, before forwarding. | Active | High |
| ACK/NACK handling | Parses or generates HL7 acknowledgements, supports original/enhanced acknowledgement options, and reports ACK success/failure. | Active - critical | Critical |
| High-fidelity mode | Aggregates messages by configured count/time constraints for high-throughput delivery; mutually exclusive with ACK mode in configuration validation/UI. | Active | High |
| Retry settings | Contracts and communication points carry retry count/delay/timeout controls used by outbound paths. | Active | High |
| Dynamic configuration cache | Caches contracts and communication points and watches JSON changes so Web-managed updates can be consumed without static recompilation. | Active - critical | Critical |

### 4.4 Output channels

| Feature | Behavior | Lifecycle | Importance |
|---|---|---|---|
| TCP sender | Sends individual or queued messages to downstream TCP endpoints, including acknowledgement handling and proxy/TLS-related configuration. | Active - critical | Critical |
| HTTP sender | Sends data with configurable HTTP details, headers, certificate/proxy support, and failure persistence. | Active - critical | Critical |
| File writer | Writes routed output to local files. | Active | Medium |
| CIM S3 sender | Converts HL7 clinical data to Avro, batches and ZIPs records, obtains presigned URLs, uploads, and records failures. | Active - critical | Critical |
| Deferred CIM upload | For `cim_s3` points with `UploadHoldSeconds > 0`, creates a dedicated ordered background flusher rather than uploading immediately. | Active | High |
| Cart Gateway model | Not part of the active Agent runtime path in this workspace; references are limited to documentation/WebAgent surfaces. Treat as product-decision backlog, not an Agent implementation gap. | Out of active Agent scope | Low |

### 4.5 Failure handling and persistence

| Feature | Behavior | Lifecycle | Importance |
|---|---|---|---|
| PostgreSQL failed-message storage | Persists failed output payloads and sender identifiers using Npgsql when database support is enabled. | Active - critical | Critical |
| Database-disabled mode | Registers `NullDatabaseUtils` so routing can run without persistence when explicitly disabled. | Active | Medium |
| Secure database credentials | Decrypts DPAPI-protected database credentials at runtime rather than requiring plaintext settings. | Active - critical | Critical |
| File retention | Builds retention policies from file communication points and deletes processed files older than configured retention days. | Active | Medium |

## 5. Clinical Insights and Cloud Data Features

### 5.1 HL7-to-Avro transformation

The shared CIM library contains the production transformation pipeline. It parses HL7 data, builds typed Avro records, splits records by size, and tracks conversion metrics.

| Mapping/capability | Behavior | Lifecycle | Importance |
|---|---|---|---|
| Patient mapping | Converts patient identity/demographic and patient-time information. | Active - critical | Critical |
| Numeric/vital mapping | Converts numeric observations and vital-sign measurements. | Active - critical | Critical |
| Waveform mapping | Converts waveform/signal data for Avro output. | Active - critical | Critical |
| Alert/alarm mapping | Converts clinical alerts and maps alarm announcement timing. | Active - critical | Critical |
| Source-device mapping | Adds source device metadata to output records. | Active | High |
| Schema-based serialization | Uses `PayloadSchema.avsc` and Apache Avro serialization. | Active - critical | Critical |
| Record chunking | Computes Avro record sizes and divides large record sets into uploadable chunks. | Active | High |
| Multi-file parsing | Supports processing HL7 batches from multiple files. | Active | Medium |

### 5.2 Cloud batch and upload pipeline

| Feature | Behavior | Lifecycle | Importance |
|---|---|---|---|
| ZIP staging | Stages serialized output into ZIP archives before cloud delivery. | Active - critical | Critical |
| Presigned upload | Requests/uses presigned URLs and performs HTTP-based S3-compatible upload. | Active - critical | Critical |
| Folder UUID rotation | Rotates cloud folder/batch identifiers after upload. | Active | Medium |
| RabbitMQ completion notification | Publishes a message after successful S3 upload through the configured RabbitMQ URL/token flow. | Active | High |
| Cloud authentication/request signing | Uses certificate/JWT/token helpers for HSDP/CIM API interactions. | Active - critical | Critical |

### 5.3 Forward Service

| Feature | Behavior | Lifecycle | Importance |
|---|---|---|---|
| Store-and-forward replay | Reads failed records from PostgreSQL and retries delivery independently of the primary Agent. | Active - critical | Critical |
| Sender-specific replay | Dispatches replay based on stored communication-point type, including CIM S3, HTTP, and TCP retry senders. | Active - critical | Critical |
| Successful-record cleanup | Removes successfully replayed records from the failed-data store. | Active - critical | Critical |
| Proxy-aware HTTP retries | Applies configured outbound proxy options during replay. | Active | High |
| Windows Service operation | Runs as `Philips.IBE.Forward`, installed and started by the installer. | Active - critical | Critical |
| Optional OTLP logs | Exports Forward Service logs to the local OpenTelemetry Collector when enabled. | Active | Medium |

### 5.4 CIM tenant onboarding

The onboarding console application orchestrates tenant setup against Philips identity/platform APIs.

| Feature | Behavior | Lifecycle | Importance |
|---|---|---|---|
| Environment selection/configuration | Uses environment-specific IAM/IDM endpoints and onboarding settings. | Supporting | Medium |
| Authentication | Obtains tokens for HSDP API operations. | Supporting | High |
| Application/proposition provisioning | Creates or configures application and proposition resources. | Supporting | Medium |
| Organization and group setup | Creates/associates organizations, groups, and role-related structures. | Supporting | Medium |
| Service key management | Creates and retrieves service-account keys. | Supporting | High |
| Certificate creation | Generates certificate material required by the provisioned integration. | Supporting | High |
| Onboarding result logging | Produces operational results and error logs for the provisioning run. | Supporting | Medium |

### 5.5 Cloud License Updater

| Feature | Behavior | Lifecycle | Importance |
|---|---|---|---|
| Interactive environment selection | Selects test/development/production cloud endpoints. | Supporting | Medium |
| Tenant metadata collection | Collects service, tenant, collector, and institution identifiers. | Supporting | Medium |
| JWT and access-token flow | Creates a JWT and exchanges credentials with IAM. | Supporting | High |
| Clinical Insights license update | Calls the Clinical Insights Gateway to update tenant/clinical-unit license information. | Supporting | High |
| User interruption/error handling | Supports cancellation and reports failed update stages. | Supporting | Medium |

### 5.6 Standalone HL7-to-Avro Converter

This project contains a second copy/generation of parser and mapper code and is published by `build.bat`. Its `Program.cs`, however, comments out `DataProcessor`, `TcpNode`, and Windows Service registration. The resulting host can start but does no conversion work through a registered listener.

**Assessment:** **Partial/incomplete**. Do not represent it as an operational conversion service until an entry path is restored and tested. The active conversion capability resides in `CIM.Common` and the primary Agent's CIM S3 flow.

## 6. Web Management Features

### 6.1 Authentication and authorization

| Feature | Behavior | Lifecycle | Importance |
|---|---|---|---|
| Login | Authenticates users and issues signed JWT bearer tokens. | Active - critical | Critical |
| AD/group-based roles | Maps configured admin and normal-user groups to role claims. | Active - critical | Critical |
| Role-based UI access | Admin routes cover service, communication-point, and contract configuration; normal routes cover transactions and heartbeat. | Active | High |
| Logout/token invalidation | Adds logged-out JWTs to an in-memory blacklist. Middleware rejects blacklisted tokens. | Active | High |
| Expired-token cleanup | Removes expired entries from the in-memory blacklist. | Active | Medium |
| Angular auth guard/interceptor | Protects routes and attaches authentication tokens to API requests. | Active | High |
| Web license enforcement | Validates the Agent product license before starting the Web API. | Active - critical | Critical |

### 6.2 Configuration APIs and screens

| Feature | API/UI behavior | Lifecycle | Importance |
|---|---|---|---|
| Communication-point management | List/get/create/update/delete TCP, HTTP, WebSocket, file/CIM-oriented endpoint configuration; protects and sanitizes sensitive values. | Active - critical | Critical |
| Contract management | List/create/update/delete routing contracts, ACK options, high-fidelity settings, and input/output associations. | Active - critical | Critical |
| Service-node management | Reads service configuration and updates HTTP, TCP, WebSocket, and ADT node settings. | Active | High |
| Proxy configuration | UI and API models support enabling proxy address/port and protected credentials for applicable nodes/points. | Active | High |
| Certificate management | Uploads single or multiple certificates and deletes certificate files or folders; admin-only. | Active - critical | Critical |
| Secret protection | Uses server-side data protection when storing sensitive communication configuration and removes protected values from responses where appropriate. | Active - critical | Critical |
| JSON persistence | Updates the Agent's configuration/contract files used by the dynamic cache. | Active - critical | Critical |

### 6.3 Monitoring and operations APIs/screens

| Feature | API/UI behavior | Lifecycle | Importance |
|---|---|---|---|
| Error queue | Lists failed transactions and updates/requeues a transaction by ID. Enabled through the `DatabaseEnabled` feature gate. | Active | High |
| Server heartbeat | Checks configured server-side service endpoints/availability. | Active | High |
| Client heartbeat | Exposes client-oriented heartbeat state. | Active | High |
| Homepage/navigation | Hosts authenticated navigation to service, communication points, contracts, transactions, and heartbeat views. | Active | Medium |
| Notifications and dialogs | Displays operation results, confirmations, validation feedback, and delete warnings. | Active | Medium |
| Static SPA hosting | Serves the Angular production bundle from `wwwroot` with fallback to `index.html`. | Active | High |
| Swagger/OpenAPI | Generates API metadata and exposes Swagger UI at the application root in Development only. | Supporting | Medium |
| CORS | Allows cross-origin calls for the Angular/API development and deployment arrangement. | Active | Medium |

### 6.4 Web persistence

| Feature | Behavior | Lifecycle | Importance |
|---|---|---|---|
| SQLite transaction/error database | Uses a local SQLite implementation behind `IDBUtils` for Web error-queue operations. | Active | High |
| Database feature switch | `DatabaseEnabled` can disable the Error Queue controller through feature management. | Active | Medium |

## 7. Security Features

| Feature | Behavior | Lifecycle | Importance |
|---|---|---|---|
| TLS 1.2 Web minimum | Configures Kestrel HTTPS defaults to TLS 1.2. | Active - critical | Critical |
| Per-endpoint TLS/certificates | TCP, HTTP, WebSocket, and S3-related models support certificate-based secure communication. | Active - critical | Critical |
| X.509 management API | Admin-only certificate upload/delete operations. | Active | High |
| Certificate generation utility | Creates a self-signed CA plus server/client certificates and exports installable material. | Supporting | High |
| DPAPI configuration encryption | PowerShell recursively encrypts password-named JSON fields with LocalMachine scope; .NET utilities decrypt at runtime. | Active - critical | Critical |
| Web JWT validation | Validates issuer, audience, lifetime, signing key, and role claims. | Active - critical | Critical |
| JWT logout blacklist | Prevents reuse of explicitly logged-out tokens for the lifetime of the Web process. | Active | High |
| License signature/expiry validation | Protects Agent and Web startup and validates installer-supplied license files. | Active - critical | Critical |
| Outbound proxy credentials | Supports protected proxy configuration for controlled enterprise egress. | Active | High |

## 8. Observability and Diagnostics

| Feature | Behavior | Lifecycle | Importance |
|---|---|---|---|
| NLog file/console logging | Produces structured operational logs with configured levels and rotation. | Active - critical | Critical |
| Optional OTLP logs | Sends Agent and Forward logs to `http://localhost:4318/v1/logs`. | Active | High |
| Optional OTLP metrics | Sends Agent metrics to `http://localhost:4318/v1/metrics`, currently every 10 seconds. | Active | High |
| Runtime metrics | Adds .NET runtime instrumentation to the Agent meter provider. | Active | Medium |
| Message counters | Tracks received, processed, sent, filtered, failed, ACK success/failure, and message types. | Active - critical | Critical |
| CIM conversion metrics | Counts generated Avro record categories and batch outcomes. | Active | High |
| Throughput reporting | Reports per-minute conversion/upload/end-to-end timing and rates for real load. | Active | High |
| OpenTelemetry Collector installer | Installer script can install a bundled `otelcol-contrib` MSI as a Windows service. | Supporting | High |
| Metrics viewer | PowerShell viewer reads archived `ibe_metrics` JSON data and presents date/hour selections. It exists but is not copied by the current `build.bat`. | Supporting, packaging gap | Low |
| TCP test harness timing | Logs downstream ACK and round-trip timing for end-to-end message tests. | Supporting | Medium |
| Postman collection | Provides CIM/API requests for manual integration testing. | Supporting | Medium |

## 9. Installation, Build, and Operations

### 9.1 Build and packaging

`src/build.bat` performs the current Windows release build:

1. Recreates `publish/` and `HA/` output directories.
2. Copies `PasswordEncryptor.ps1`, `getLicenseData.ps1`, `ServiceInstaller.ps1`, and `CertificateGenerator.ps1` into the package.
3. Copies all HA scripts into an independent `HA/` folder.
4. Restores and publishes Onboarding Service, Cloud License Updater, Agent, Forward Service, standalone Avro Converter, and Web API for `win-x64`.
5. Installs Angular dependencies with `--legacy-peer-deps`, performs a production build, and copies the browser bundle into Web `wwwroot`.

The script is Windows-only and framework-dependent (`--self-contained false`).

### 9.2 Service installation

| Feature | Behavior | Lifecycle | Importance |
|---|---|---|---|
| Administrative privilege check | Refuses service installation without elevation. | Active - critical | Critical |
| Install/reinstall/uninstall | Manages Agent, Forward, and Web Windows services through `sc.exe` and PowerShell service commands. | Active - critical | Critical |
| Automatic Agent startup | Creates the Agent with automatic startup. | Active - critical | Critical |
| License-gated Agent install | Validates the selected license through the Agent executable; rolls back the service on failure. | Active - critical | Critical |
| License path injection | Writes the validated license path into Agent `appsettings.json`. | Active | High |
| Start timeout and logging | Waits for service startup and appends timestamped results to `InstallLogs.log`. | Active | High |
| OpenTelemetry Collector installation | Optionally installs/reinstalls a bundled collector MSI. | Supporting | High |

### 9.3 License administration

| Feature | Behavior | Lifecycle | Importance |
|---|---|---|---|
| License request collection | `getLicenseData.ps1` collects customer/contact/order/agent/MAC data and creates a Base64-encoded request file. | Supporting | High |
| Development license generation | Generates a signed license with configured development validity. | Supporting | High |
| Production license generation | Generates a signed license with configured production validity. | Supporting | High |
| Certificate-store signing | Retrieves signing material by certificate thumbprint and uses RSA/X.509 operations. | Supporting | Critical |
| DPAPI support in licensing tool | Protects sensitive local license-generation data. | Supporting | High |

## 10. PostgreSQL High Availability

| Feature | Behavior | Lifecycle | Importance |
|---|---|---|---|
| Failover orchestration | Coordinates standby promotion after primary failure, monitors the old primary, and initiates its return as a standby. | Active - critical | Critical |
| Standby promotion | Validates PostgreSQL paths/state, runs `pg_ctl promote`, and verifies removal of `standby.signal`. | Active - critical | Critical |
| Old-primary rewind | Stops PostgreSQL, runs `pg_rewind` against the new primary, recreates standby settings, and restarts replication. | Active - critical | Critical |
| HA logging | Writes promotion/rewind/orchestration actions to dedicated failover logs. | Active | High |
| Registry-based config update | Reads service secrets from `HKLM` and updates Agent/Forward JSON database passwords. | Supporting | High |
| Streaming-replication assumptions | Scripts assume PostgreSQL 18 paths, Windows services, network reachability, and predefined host/IP/account details. | Active constraint | Critical |

## 11. Test and Verification Features

| Test asset | Coverage/purpose | Lifecycle | Importance |
|---|---|---|---|
| Web controller unit tests | Certificate, communication point, contract, error queue, heartbeat, login, and service-node behavior. | Supporting | High |
| JWT invalidator tests | Blacklist insertion, lookup, expiry cleanup, and middleware rejection. | Supporting | High |
| Web service/DB tests | Configuration services and SQLite behavior. | Supporting | High |
| Angular component/service specs | Authentication, guards/interceptors, API services, pages, dialogs, and configuration models. | Supporting | High |
| TCP harness server mode | Emulates a downstream MLLP receiver and returns `MSA|AA` ACKs. | Supporting | Medium |
| TCP harness client mode | Sends generated ADT messages to the Agent and waits for acknowledgements. | Supporting | Medium |
| TCP harness combined/load mode | Runs both ends and supports message count/delay controls for repeated flow testing. | Supporting | High |
| Coverage artifacts | Existing Angular and .NET coverage outputs indicate prior test execution; they are generated artifacts, not source features. | Supporting evidence | Low |

## 12. Legacy Service.Agent Feature Set

`src/Philips.IBE.Service.Agent/IBEAgent` is a separate, older implementation. It remains compilable source but is not referenced by the current `build.bat` or `ServiceInstaller.ps1`.

| Legacy feature | Behavior | Assessment |
|---|---|---|
| CloudBridge manager | Centralized older cloud routing architecture. | Legacy; superseded by current nodes/DataProcessor/CIM pipeline |
| Legacy TCP/HTTP listeners | Receives and routes messages through older managers and models. | Legacy |
| Legacy message processor | Queue-based output and failure handling using the prior configuration shape. | Legacy |
| SQLite-oriented database handler | Persists failures using the older local database approach. | Legacy |
| ReportSync | Receives reports over WebSocket and relays to TCP with ACK/retry behavior. | Legacy but functionally distinct; confirm no deployed dependency before removal |
| Legacy proxy handling | Configures proxy behavior for older WebSocket/HTTP paths. | Legacy |
| Secure configuration scripts | Protects passwords and communication data for the old configuration layout. | Legacy supporting tooling |

**Recommendation:** establish whether any installed customer version still depends on ReportSync or the legacy configuration schema. If not, archive or remove this solution to reduce duplicated models, parsers, database abstractions, and security maintenance.

## 13. Partial, Stale, and Removal Candidates

| Item | Evidence-based assessment | Recommended action |
|---|---|---|
| Standalone HL7-to-Avro service | No active standalone converter project was found in this checkout outside the Agent HL7/CIM codec path. | Treat as absent from active Agent scope; restore only if product ownership supplies the missing project/package requirement. |
| Legacy Service.Agent | Separate superseded architecture; absent from current build/install. | Confirm deployment usage, then archive/remove. |
| Nested `src/app/Philips.IBE.Service.WebAgent.sln` | Solution file inside Angular source; no current build role found. | Verify IDE use; likely remove. |
| Root `src/CertificateGenerator.ps1` duplicate | Current build packages the copy under `Installation Script/`. | Compare contents/history and retain one canonical script. |
| MetricsViewer packaging | No MetricsViewer project/directory was found in this checkout. | Treat as absent from active Agent scope; add packaging only if the viewer project is restored. |
| Cart Gateway configuration | No active Agent runtime model/sender/dispatcher exists outside WebAgent; no checked-in Agent path references it. | Keep out of Agent runtime scope until product ownership confirms a committed sender requirement. |
| Duplicated Avro converter code | Active non-WebAgent code in this checkout contains a single Agent HL7/CIM codec path; no duplicate converter project was found. | No active-code consolidation needed in this workspace. |
| Multiple DB abstractions/models | Agent/Common and Web maintain separate PostgreSQL/SQLite abstractions and duplicated config shapes. | Keep if ownership boundaries are intentional; otherwise consolidate cautiously. |
| README versions | README says .NET 8/Angular 18, manifests use .NET 10/Angular 21. | Update documentation to match build inputs. |
| Hard-coded OTLP endpoint | Agent/Forward target local collector URLs. | Make endpoint/protocol/export interval configurable. |
| Hard-coded HA environment details | PostgreSQL 18 path and host/IP assumptions are embedded in scripts. | Externalize into validated HA configuration. |
| In-memory JWT blacklist | Logout invalidation is lost on Web restart and is not shared across HA nodes. | Use a distributed or persistent revocation strategy if Web runs multi-node. |

## 14. External Integration Inventory

| Integration | Used for | Owning areas | Criticality |
|---|---|---|---|
| HL7 v2 / MLLP | Clinical message ingestion, routing, ACKs, filtering | Agent, Common, test harness, legacy Agent | Critical |
| Apache Avro | Typed clinical payload serialization | CIM.Common, Agent CIM path, converter scaffold | Critical |
| S3-compatible presigned upload | Cloud batch delivery | CIM.Common, Agent, Forward | Critical |
| RabbitMQ endpoint | Post-upload notification | CIM.Common S3 utility | High |
| PostgreSQL | Failed-message persistence and replay | Agent.Common, Agent, Forward | Critical |
| SQLite | Web transaction/error queue | Web Server | High |
| Philips IAM/IDM/HSDP | Authentication and tenant provisioning | Onboarding, Cloud License Updater, CIM upload helpers | Critical |
| Clinical Insights Gateway | Cloud license information update | Cloud License Updater | High |
| Windows Active Directory | Web user/group authentication | Web Server | Critical |
| Windows Certificate Store/X.509 | TLS, signing, license generation | Agent, Web, Licensing, scripts | Critical |
| Windows DPAPI | At-rest protection for JSON credentials | Agent.Common, Web, licensing/scripts | Critical |
| OpenTelemetry Collector | Metrics and logs | Agent, Forward, installer | High |
| Corporate HTTP proxy | Controlled outbound connectivity | Agent nodes/senders, Forward, Web configuration | High |

## 15. Configuration Inventory

| Configuration | Controls | Status |
|---|---|---|
| Agent `appsettings.json` | Logging, observability, node switches, workflow paths, database, license, proxy-related settings | Active - critical |
| `communicationData.json` | Communication points and protocol-specific connection data | Active - critical |
| `contractData.json` | Input/output routing, ACK, high-fidelity, and filters | Active - critical |
| `filterHL7.json` | HL7 filtering examples/settings | Supporting |
| Forward `appsettings.json` | Database, workflow paths, retry/observability context | Active - critical |
| Web `appsettings.json` | Kestrel, JWT, authentication mode/groups, database feature, config folders, license | Active - critical |
| Onboarding `appsettings.json` | Environment IAM/IDM and provisioning endpoints/options | Supporting |
| Cloud Updater `appsettings.json` | IAM and Clinical Insights endpoints by environment | Supporting |
| Licensing `Configuration.json` | Signing certificate thumbprint and license validity periods | Supporting - sensitive |
| Legacy `ServiceConfigurations.json` | Old listeners, CloudBridge, database, WebSocket, proxy, and ReportSync | Legacy |

## 16. Importance-Based View

### Critical product capabilities

- Contract-driven HL7 routing across TCP/ADT/HTTP/WebSocket/File inputs.
- TCP, HTTP, File, and CIM S3 outputs.
- HL7 filtering, ACK/NACK behavior, and high-fidelity batching.
- HL7-to-Avro clinical mapping and cloud ZIP upload.
- PostgreSQL-backed store-and-forward with the Forward Service.
- License validation, JWT/AD authorization, TLS/certificates, and DPAPI secret protection.
- Web configuration management for communication points, contracts, nodes, and certificates.
- Windows service deployment and PostgreSQL HA scripts.

### Important supporting capabilities

- Error queue, heartbeat, metrics, logging, throughput reporting, and OpenTelemetry.
- Tenant onboarding and cloud-license update utilities.
- Local license request/generation tools.
- TCP/MLLP harness, unit/frontend tests, Swagger, and Postman requests.
- Certificate generation, password encryption, and configuration-update scripts.

### Not currently required for the main runtime path

- Legacy Service.Agent and ReportSync implementation, unless older field deployments still use it.
- Standalone Avro Converter host in its current no-op state.
- Nested Angular solution and duplicate certificate script.
- Cart Gateway model until product ownership confirms it belongs in the active Agent runtime.

## 17. Confidence and Limits

This is a source-complete inventory for the checked-out workspace: all manifests and major implementation surfaces were included, and the results were reconciled against GitNexus execution flows. “Active” means connected to the checked-in build, startup, installer, controller, UI route, or a directly invoked shared path. It does not prove that every feature is enabled in every deployed environment.

Items labeled Legacy, Partial, or Candidate for removal require confirmation from release history, customer deployment records, and product ownership before deletion. In particular, ReportSync may represent an older but still contractually supported field workflow, and HA scripts contain environment-specific assumptions that source inspection alone cannot validate.
