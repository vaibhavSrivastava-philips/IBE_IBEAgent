# IBE Agent - End-to-End Test Workflow

This harness drives real messages through the IBE Agent over its actual TCP and
HTTP transports and verifies, end to end, that:

- messages sent to an inbound comm point are delivered to the configured
  downstream comm point(s), and
- the source receives exactly the acknowledgement or response its contract
  promises for each acknowledgement mode.

It is intended to be run as a repeatable acceptance check and to be readable at a
glance during a review. Every run produces a summary table and a full evidence
trail on disk.

## What it covers

The workflow runs a matrix of scenarios defined in [scenarios.psd1](scenarios.psd1):

| Dimension          | Values                                        |
| ------------------ | --------------------------------------------- |
| Input transport    | TCP (MLLP), HTTP                              |
| Output transport   | TCP (MLLP), HTTP, and TCP + HTTP fan-out       |
| Acknowledgement    | none, normal, enhanced, response              |

Acknowledgement modes and what the source is expected to observe:

| Mode       | Source observes                                                            |
| ---------- | ------------------------------------------------------------------------- |
| `none`     | Nothing over TCP. Over HTTP the request is held, then released with `504`. The message is still delivered downstream. |
| `normal`   | A fixed "received" acknowledgement, returned on acceptance, independent of delivery. |
| `enhanced` | A delivery acknowledgement (`MSA|AA|<correlation-id>`) returned only after the required leg has actually delivered. |
| `response` | The downstream system's own response payload, relayed back to the source. |

## How it is put together

The harness models the two ends of the flow as independent "comm point" scripts,
so each can be run and understood on its own:

| Script                              | Role                                                        |
| ----------------------------------- | ----------------------------------------------------------- |
| `peers/Start-TcpReceiver.ps1`       | Downstream TCP/MLLP system. Records deliveries, replies with an MLLP acknowledgement. |
| `peers/Start-HttpReceiver.ps1`      | Downstream HTTP system. Records deliveries, returns a response body. |
| `peers/Send-TcpMessage.ps1`         | Upstream TCP/MLLP system. Sends one message, reads the acknowledgement. |
| `peers/Send-HttpMessage.ps1`        | Upstream HTTP system. Posts one message, captures the response. |

[Invoke-E2EWorkflow.ps1](Invoke-E2EWorkflow.ps1) is the orchestrator. For each
scenario it generates a `contractData.json`, starts a fresh agent instance with
that topology, drives one uniquely marked HL7 message through it, and checks both
the downstream capture files and the source-side reply.

The downstream comm points run for the whole session and record every message
they receive. The upstream comm points are invoked once per scenario.

## Running it

Prerequisites: the .NET SDK used by the repository, and PowerShell 7 (`pwsh`).

```powershell
# From the repository root
pwsh -File tests/e2e/Invoke-E2EWorkflow.ps1
```

Useful options:

```powershell
# Run a subset by name (wildcards)
pwsh -File tests/e2e/Invoke-E2EWorkflow.ps1 -Only '*enhanced*'
pwsh -File tests/e2e/Invoke-E2EWorkflow.ps1 -Only 'TCP in, TCP out*'

# Reuse the previously published agent (skip the publish step)
pwsh -File tests/e2e/Invoke-E2EWorkflow.ps1 -SkipBuild

# Publish and test the Release build
pwsh -File tests/e2e/Invoke-E2EWorkflow.ps1 -Configuration Release
```

The process exit code is `0` when every scenario passes and `1` otherwise, so the
workflow can be wired into a pipeline.

## Coverage report

The repository includes a build-integrated coverage target for the active non-WebAgent
test projects. It runs unit/integration tests with Coverlet, merges the results with
ReportGenerator, and writes HTML plus Cobertura outputs under `artifacts/coverage/`.

```powershell
# Restore local tools once per clone
dotnet tool restore

# From the repository root. Uses the active non-WebAgent test projects.
dotnet build tools/coverage/CoverageReport.proj -t:CoverageReport

# Release configuration
dotnet build tools/coverage/CoverageReport.proj -t:CoverageReport -p:Configuration=Release

# Reuse already-built test binaries
dotnet build tools/coverage/CoverageReport.proj -t:CoverageReport -p:CoverageSkipBuild=true
```

Open `artifacts/coverage/report/index.html` for the merged HTML report. The merged
Cobertura file is `artifacts/coverage/report/Cobertura.xml`.

## File comm points

The File inbound (folder poller) and File outbound (file writer) comm points have
their own workflow, [Invoke-FileE2EWorkflow.ps1](Invoke-FileE2EWorkflow.ps1), driven
by [file-scenarios.psd1](file-scenarios.psd1). It reuses this harness's `lib/` helpers
and the TCP/HTTP downstream peers, and adds File-specific helpers in
[lib/FileCommon.ps1](lib/FileCommon.ps1) plus a "file drop" upstream peer,
[peers/Send-FileMessage.ps1](peers/Send-FileMessage.ps1).

A File source has no reply channel, so instead of a source-side reply the workflow
verifies the **disposition** of the input file on disk. File deliveries are verified
by polling the output directory (no long-lived listener is needed).

```powershell
# From the repository root
pwsh -File tests/e2e/Invoke-FileE2EWorkflow.ps1
pwsh -File tests/e2e/Invoke-FileE2EWorkflow.ps1 -Only '*Watermark*' -SkipBuild
```

What it covers:

| Group                   | Scenarios                                                                                                                                                                                                                     |
| ----------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Relay / cross-transport | File to File, File to TCP, File to HTTP, TCP to File, HTTP to File                                                                                                                                                            |
| Disposition             | Move to `processed/` (on delivery), Move to `error/` (delivery to a dead port fails), Watermark (file left in place, `.lastProcessedTime` advanced, not re-read). Driven by the `KeepOriginalFiles` knob: `false` -> Move, `true` -> Watermark; Watermark auto-arms the marker to "now" on first start (pre-existing backlog skipped). |
| Content                 | base64 blob envelope (`{filename, filecontent, destinationpath}`) decoded by the `blob-envelope-extract` pipeline — the output file is named from `filename` and `destinationpath` is ignored; and a base64 payload decoded by the output leg's base64 codec |

Each scenario writes its input/output under `artifacts/file-<run-id>/s<nn>/{in,out}`.
The base64 scenarios need a base64 codec and a `blob` pipeline, which the workflow
writes into `.agent/catalogData.json` (the sandbox), leaving the repository's
`config/` untouched.

### Network shares (manual)

Authenticated UNC shares are not exercised automatically because they need a real
reachable share and credentials. To check one by hand:

1. Create or attach a share the agent's service account can reach, e.g. `\\server\ibe-in`.
2. DPAPI-protect the share password (LocalMachine scope, base64) on the agent host —
   this matches the agent's own `DataProtectorFactory`:

   ```powershell
   $bytes = [System.Text.Encoding]::UTF8.GetBytes('<share-password>')
   $protected = [System.Security.Cryptography.ProtectedData]::Protect($bytes, $null, 'LocalMachine')
   [Convert]::ToBase64String($protected)   # use this as PasswordProtected
   ```

3. Configure a File inbound with the UNC `Directory`, `Username`, `Domain`, and the
   `PasswordProtected` value above, then drop a file and confirm it moves to
   `<share>\processed\`. Forward-slash UNC paths (`//server/ibe-in`) are accepted too.

## Evidence produced

Every run writes a timestamped folder under `tests/e2e/artifacts/<run-id>/`:

| File                              | Contents                                              |
| --------------------------------- | ----------------------------------------------------- |
| `report.txt`                      | Human-readable per-scenario result and summary.       |
| `report.json`                     | The same results in machine-readable form.            |
| `workflow.log`                    | The full workflow narration.                          |
| `agent.S<nn>.out.log` / `.err.log`| The IBE Agent's own console output for each scenario.  |
| `downstream-tcp.capture.jsonl`    | Every message the TCP delivery target received.        |
| `downstream-http.capture.jsonl`   | Every message the HTTP delivery target received.       |
| `downstream-tcp.log` / `-http.log`| The downstream comm points' activity logs.             |

Each message carries a unique id (embedded in `MSH-10` and a trailing `ZMK`
segment), which is how a delivery is matched back to the scenario that produced
it.

## Ports used

The workflow uses loopback ports only:

| Purpose                          | Port / prefix                          |
| -------------------------------- | -------------------------------------- |
| Downstream TCP delivery target   | `127.0.0.1:17001`                      |
| Downstream HTTP delivery target  | `http://localhost:19090/ibe/`          |
| Per-scenario TCP inbound         | `127.0.0.1:1600<n>`                    |
| Per-scenario HTTP inbound        | `http://localhost:1800<n>/ibe/`        |

If a port is already in use, the affected scenario reports the agent endpoint as
not ready and the run continues. HTTP endpoints bind `localhost` prefixes, which
Windows permits for the current user without an elevated URL reservation.

## Notes

- The agent is published once per run to `tests/e2e/.agent/`; the harness writes a
  per-scenario `contractData.json` there and never modifies the repository's
  `config/` files.
- `artifacts/` and `.agent/` are generated and are excluded from source control.
