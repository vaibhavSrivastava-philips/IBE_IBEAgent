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
