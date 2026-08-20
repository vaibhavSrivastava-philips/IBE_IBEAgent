# IBE Agent - Demonstration Kit

A small, presentable setup for driving real HL7 messages through the IBE Agent
over its actual TCP and HTTP transports. You configure a contract, start the
matching external systems (simple scripts that stand in for an upstream sender
and a downstream receiver), start the agent, and send messages by hand.

Everything runs on `localhost`, so it can be shown on a single machine.

## The picture

```
   UPSTREAM (external script)          IBE AGENT (owns these comm points)        DOWNSTREAM (external script)
   ---------------------------         ------------------------------------      ----------------------------
   Send-TcpMessage.ps1  --------->  TCP inbound  :5101   ]                 [   TCP outbound  -> 127.0.0.1:5201  --------->  Start-TcpReceiver.ps1
                                                          }  contracts route  {
   Send-HttpMessage.ps1 --------->  HTTP inbound :5102   ]   and fan out      [   HTTP outbound -> :5202/ibe/inbound --->  Start-HttpReceiver.ps1
   Send-WebSocketMessage.ps1 ---->  WS inbound   :5103   ]                 [   WS outbound   -> :5203/ibe/ws/     --->  Start-WebSocketReceiver.ps1
```

The agent **owns** the four comm points declared in
[config/contractData.json](../config/contractData.json): it listens on the two
inbound ports and connects out to the two outbound addresses. The **external**
systems are the scripts in this folder:

| Comm point            | Role                                  | Address                            | Script |
| --------------------- | ------------------------------------- | ---------------------------------- | ------ |
| TCP inbound (id 1)    | agent listens; peer sends in          | `127.0.0.1:5101`                   | [upstream/Send-TcpMessage.ps1](upstream/Send-TcpMessage.ps1) |
| HTTP inbound (id 2)   | agent listens; peer posts in          | `http://localhost:5102/ibe/`       | [upstream/Send-HttpMessage.ps1](upstream/Send-HttpMessage.ps1) |
| TCP outbound (id 101) | agent connects out; peer receives     | `127.0.0.1:5201`                   | [downstream/Start-TcpReceiver.ps1](downstream/Start-TcpReceiver.ps1) |
| HTTP outbound (id 102)| agent connects out; peer receives     | `http://localhost:5202/ibe/inbound`| [downstream/Start-HttpReceiver.ps1](downstream/Start-HttpReceiver.ps1) |
| WebSocket inbound (id 7)  | agent listens; peer sends in      | `ws://localhost:5103/ibe/ws/`      | [upstream/Send-WebSocketMessage.ps1](upstream/Send-WebSocketMessage.ps1) |
| WebSocket outbound (id 107)| agent connects out; peer receives | `ws://localhost:5203/ibe/ws/`      | [downstream/Start-WebSocketReceiver.ps1](downstream/Start-WebSocketReceiver.ps1) |

## Prerequisites

- The .NET SDK used by the repository.
- PowerShell 7 (`pwsh`).

## Running a demonstration

Use a separate terminal for each long-running piece. The order matters: start the
downstream receivers first, then the agent, then the upstream sender.

1. **Start the downstream receiver(s)** your contract delivers to. Start only the
   ones the contract actually uses.

   ```powershell
   pwsh -File demo/downstream/Start-TcpReceiver.ps1     # if the contract has a TCP output (id 101)
   pwsh -File demo/downstream/Start-HttpReceiver.ps1    # if the contract has an HTTP output (id 102)
   pwsh -File demo/downstream/Start-WebSocketReceiver.ps1  # if the contract has a WebSocket output (id 107)
   ```

2. **Start the agent** against the repository configuration:

   ```powershell
   pwsh -File demo/Start-Agent.ps1
   ```

   It prints a startup summary such as `IBE Agent started: 2 contract(s), 2 inbound endpoint(s).`

3. **Start the upstream sender** for the input your contract uses, then press
   Enter to send a message (type `Q` then Enter to quit):

   ```powershell
   pwsh -File demo/upstream/Send-TcpMessage.ps1     # feeds the TCP input (id 1)
   pwsh -File demo/upstream/Send-HttpMessage.ps1    # feeds the HTTP input (id 2)
   pwsh -File demo/upstream/Send-WebSocketMessage.ps1   # feeds the WebSocket input (id 7)
   ```

Each Enter sends one HL7 ADT message (from [messages/adt-a01.hl7](messages/adt-a01.hl7),
with a fresh control id). You will see the message leave the sender, arrive at the
downstream receiver, and the acknowledgement or response come back to the sender.

> After editing `config/contractData.json`, stop the agent (Ctrl+C) and start it
> again. The agent reads its configuration once at startup.

## Configuring the contract for any scenario

A contract lives in the `Contracts` array of
[config/contractData.json](../config/contractData.json). Three choices define a
scenario: which **input(s)** feed it, which **output(s)** it delivers to, and the
**reply mode**.

### Choose the input(s)

```jsonc
"Inputs": [ { "InputId": 1 } ]                     // TCP input
"Inputs": [ { "InputId": 2 } ]                     // HTTP input
"Inputs": [ { "InputId": 1 }, { "InputId": 2 } ]   // both inputs feed the same contract
```

### Choose the output(s)

```jsonc
"Outputs": [ { "OutputId": 101 } ]                       // TCP output only
"Outputs": [ { "OutputId": 102 } ]                       // HTTP output only
"Outputs": [ { "OutputId": 101 }, { "OutputId": 102 } ]  // fan-out: deliver to BOTH
```

Optionally, restrict a leg to certain inputs (useful with multiple inputs):

```jsonc
"Outputs": [
  { "OutputId": 101, "FromInputIds": [ 1 ] },   // only messages from input 1 go to the TCP output
  { "OutputId": 102, "FromInputIds": [ 2 ] }    // only messages from input 2 go to the HTTP output
]
```

### Choose the reply mode

Add exactly one of these to the contract:

| Mode         | What the source sees                                                | JSON |
| ------------ | ------------------------------------------------------------------- | ---- |
| No ack       | TCP: nothing. HTTP: `504` after the reply timeout. Still delivered. | `"Acknowledgement": { "IsEnabled": false }` |
| Normal ack   | A fixed "received" ack, returned on acceptance.                     | `"Acknowledgement": { "IsEnabled": true, "IsEnhanced": false, "Shape": "Single" }` |
| Enhanced ack | An ack reflecting the real delivery outcome, after the leg settles. | `"Acknowledgement": { "IsEnabled": true, "IsEnhanced": true, "Shape": "Single" }` |
| Response     | The downstream system's own reply, relayed to the source.           | `"Acknowledgement": { "IsEnabled": false }, "Response": { "IsEnabled": true, "TimeoutMs": 15000 }` |

Response mode requires exactly one output leg (the responder).

### Worked examples

**TCP in, fan-out to TCP + HTTP, enhanced ack** (one send fans out to both downstream systems):

```jsonc
{
  "Name": "Demo-Fanout-Enhanced",
  "Template": "adt",
  "Inputs": [ { "InputId": 1 } ],
  "Acknowledgement": { "IsEnabled": true, "IsEnhanced": true, "Shape": "Single" },
  "Outputs": [ { "OutputId": 101 }, { "OutputId": 102 } ]
}
```

Run: `Start-TcpReceiver.ps1` + `Start-HttpReceiver.ps1` + agent + `Send-TcpMessage.ps1`.

**Two inputs into one HTTP output, normal ack** (a single downstream aggregates both feeds):

```jsonc
{
  "Name": "Demo-MultiInput-Normal",
  "Template": "adt",
  "Inputs": [ { "InputId": 1 }, { "InputId": 2 } ],
  "Acknowledgement": { "IsEnabled": true, "IsEnhanced": false, "Shape": "Single" },
  "Outputs": [ { "OutputId": 102 } ]
}
```

Run: `Start-HttpReceiver.ps1` + agent + either `Send-TcpMessage.ps1` or `Send-HttpMessage.ps1`.

**HTTP in, TCP out, request-reply** (the caller gets the downstream system's own response):

```jsonc
{
  "Name": "Demo-Http-Response",
  "Template": "adt",
  "Inputs": [ { "InputId": 2 } ],
  "Acknowledgement": { "IsEnabled": false },
  "Response": { "IsEnabled": true, "TimeoutMs": 15000 },
  "Outputs": [ { "OutputId": 101 } ]
}
```

Run: `Start-TcpReceiver.ps1` + agent + `Send-HttpMessage.ps1`.

### What ships by default

Two contracts are configured out of the box so all four comm points are live:

- `Demo-Tcp-To-Http-Enhanced` - TCP in (1) to HTTP out (102), enhanced ack.
- `Demo-Http-To-Tcp-Response` - HTTP in (2) to TCP out (101), response mode.

## WebSocket round-trip (ships by default)

A ready-to-run WebSocket contract is configured in
[config/contractData.json](../config/contractData.json):

```jsonc
{
  "Name": "Demo-Ws-To-Ws",
  "Workflow": { "Use": "adt" },
  "Inputs": [ { "InputId": 7 } ],                                   // WebSocket inbound :5103
  "Acknowledgement": { "IsEnabled": true, "IsEnhanced": true, "Shape": "Single" },
  "Response": { "IsEnabled": false },
  "Outputs": [
    {
      "OutputId": 107,                                              // WebSocket outbound :5203
      "DeliveryGuarantee": "AtLeastOnce",
      "Retry": { "MaxAttempts": 3, "BackoffSeconds": 2, "Backoff": "Exponential" }
    }
  ]
}
```

A WebSocket message is **one HL7 message per binary frame** (no MLLP framing). The agent hosts the
inbound WS server, dials the outbound WS server, relays the downstream ack back to the sender (Enhanced
ack), and - because the leg is `AtLeastOnce` - **persists and replays** on delivery failure.

**Run it** (three terminals, in order):

```powershell
pwsh -File demo/downstream/Start-WebSocketReceiver.ps1   # 1. downstream WS system on :5203
pwsh -File demo/Start-Agent.ps1                          # 2. the agent
pwsh -File demo/upstream/Send-WebSocketMessage.ps1       # 3. upstream WS sender on :5103 (press Enter to send)
```

### Scenarios to test

Each row is a change to the `Demo-Ws-To-Ws` contract and/or how you start the receiver. **Restart the
agent after any config change.** The sender is always `Send-WebSocketMessage.ps1` (add `-NoAck` where noted).

| # | Scenario | Contract / receiver setup | How to trigger | Expected result |
|---|----------|---------------------------|----------------|-----------------|
| 1 | **Basic flow + Enhanced ack** (default) | ships as-is | receiver running; press Enter to send | Sender shows `MSA\|AA\|RECEIVED-BY-WS-DOWNSTREAM` (the downstream's own reply, relayed) |
| 2 | **Normal ack** | `"Acknowledgement": { "IsEnabled": true, "IsEnhanced": false, "Shape": "Single" }` | send | Sender gets a generated "received" ack immediately on acceptance (not the downstream's) |
| 3 | **No ack** | `"Acknowledgement": { "IsEnabled": false }`, `"Response": { "IsEnabled": false }` | send with `-NoAck` | Message still delivered to the receiver; sender waits for nothing |
| 4 | **Response mode** | `"Acknowledgement": { "IsEnabled": false }`, `"Response": { "IsEnabled": true, "TimeoutMs": 15000 }` | send | Downstream's reply is relayed to the sender as the response (single output only) |
| 5 | **Negative ack (NACK)** | default contract; start receiver with `-Nack` | send | Downstream returns `MSA\|AE`; with Enhanced ack the sender sees the failure outcome |
| 6 | **Fire-and-forget sink** | default contract; start receiver with `-NoReply` | send | Delivered, but no downstream reply -> Enhanced ack waits then reports timeout/failure |
| 7 | **Retry / store-and-forward** | default (`AtLeastOnce`, 3x exponential) | **stop the receiver**, send 1-2 messages, then **restart the receiver** | Delivery fails, is persisted and retried with backoff; on restart the queued messages are replayed and delivered |
| 8 | **Idle reconnect** | default contract | send, wait > a few seconds (receiver may drop idle sockets), send again | Second send still delivers (agent re-dials the WS downstream) |
| 9 | **Fan-out** | `"Outputs": [ { "OutputId": 107 }, { "OutputId": 106 } ]` | run WS **and** HTTP receivers; send | One send is delivered to both the WS and HTTP downstreams |
| 10 | **Cross-transport** | `"Inputs": [ { "InputId": 7 } ]`, `"Outputs": [ { "OutputId": 106 } ]` | run the **HTTP** receiver; send over WS | WS in -> HTTP out (the engine is transport-neutral) |

> Ack semantics are identical to the other transports (see *Choose the reply mode*); WebSocket simply
> carries each HL7 message and each ack as one binary frame.

## Notes

- `Template: "adt"` (from [config/catalogData.json](../config/catalogData.json))
  supplies the HL7 v2 encoding and a pass-through pipeline, so contracts only need
  to declare inputs, outputs, and the reply mode.
- The agent's TCP outbound has `ExpectReply: true`, so it waits for the downstream
  system's MLLP acknowledgement. Keep `Start-TcpReceiver.ps1` running whenever a
  contract has a TCP output.
- Only start the downstream receivers a contract actually uses. If a contract has
  a TCP output but its receiver is not running, that delivery fails (and an
  enhanced ack would report the failure).
- Every comm point value here is loopback and chosen to avoid clashing with the
  automated matrix harness in `tests/e2e`.
