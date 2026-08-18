<#
.SYNOPSIS
    End-to-end test workflow for the IBE Agent message engine.

.DESCRIPTION
    Exercises the agent across every combination of input transport (TCP, HTTP,
    WebSocket), output transport (TCP, HTTP, WebSocket), and acknowledgement mode
    (none, normal, enhanced, response), plus fan-out topologies. For each scenario
    the workflow:

      1. Generates a contractData.json that wires an inbound comm point to one or
         more outbound comm points with the scenario's acknowledgement mode.
      2. Starts a fresh IBE Agent host instance with that configuration.
      3. Sends a uniquely marked HL7 message from a simulated upstream system.
      4. Confirms the message arrived at every downstream comm point.
      5. Confirms the source received the acknowledgement or response the mode
         promises (or nothing, for no-ack).
      6. Captures the agent's own log output for the record.

    Downstream comm points (the delivery targets) run for the whole session and
    record every message they receive. The upstream comm points (the senders)
    are invoked per scenario. All evidence is written under artifacts/<run-id>/.

.PARAMETER Only
    One or more wildcard patterns; only scenarios whose name matches are run.

.PARAMETER Configuration
    Build configuration for the agent (Debug or Release). Default: Debug.

.PARAMETER SkipBuild
    Reuse the previously published agent instead of publishing again.

.EXAMPLE
    pwsh -File tests/e2e/Invoke-E2EWorkflow.ps1

.EXAMPLE
    pwsh -File tests/e2e/Invoke-E2EWorkflow.ps1 -Only '*enhanced*'
#>
[CmdletBinding()]
param(
    [string[]]$Only,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Paths -------------------------------------------------------------------
$E2eRoot = $PSScriptRoot
. (Join-Path $E2eRoot 'lib\Common.ps1')

# Peer scripts contain no PowerShell-7-only syntax, so run them under whichever
# PowerShell is available: prefer 'pwsh' (7+) if installed, else fall back to
# Windows PowerShell 5.1 ('powershell.exe'), which every Windows host has.
$PwshExe = if (Get-Command pwsh -ErrorAction SilentlyContinue) { 'pwsh' } else { 'powershell.exe' }

$RepoRoot = (Resolve-Path (Join-Path $E2eRoot '..\..')).Path
$ServiceCsproj = Join-Path $RepoRoot 'src\Philips.IBE.IBEAgent\hosts\Philips.IBE.IBEAgent.Service\Philips.IBE.IBEAgent.Service.csproj'
$AgentDir = Join-Path $E2eRoot '.agent'
$AgentExe = Join-Path $AgentDir 'Philips.IBE.IBEAgent.Service.exe'

$PeerTcpReceiver = Join-Path $E2eRoot 'peers\Start-TcpReceiver.ps1'
$PeerHttpReceiver = Join-Path $E2eRoot 'peers\Start-HttpReceiver.ps1'
$PeerWebSocketReceiver = Join-Path $E2eRoot 'peers\Start-WebSocketReceiver.ps1'
$PeerTcpSender = Join-Path $E2eRoot 'peers\Send-TcpMessage.ps1'
$PeerHttpSender = Join-Path $E2eRoot 'peers\Send-HttpMessage.ps1'
$PeerWebSocketSender = Join-Path $E2eRoot 'peers\Send-WebSocketMessage.ps1'

$RunId = (Get-Date).ToString('yyyyMMdd-HHmmss')
$RunDir = Join-Path $E2eRoot "artifacts\$RunId"
New-Item -ItemType Directory -Path $RunDir -Force | Out-Null

$WorkflowLog = Join-Path $RunDir 'workflow.log'
$TcpCaptureFile = Join-Path $RunDir 'downstream-tcp.capture.jsonl'
$HttpCaptureFile = Join-Path $RunDir 'downstream-http.capture.jsonl'
$WebSocketCaptureFile = Join-Path $RunDir 'downstream-websocket.capture.jsonl'
$TcpReceiverLog = Join-Path $RunDir 'downstream-tcp.log'
$HttpReceiverLog = Join-Path $RunDir 'downstream-http.log'
$WebSocketReceiverLog = Join-Path $RunDir 'downstream-websocket.log'
$StopFile = Join-Path $RunDir 'STOP'

# --- Fixed topology ----------------------------------------------------------
$DownstreamTcpPort = 17001
$DownstreamHttpPrefix = 'http://localhost:19090/ibe/'
$DownstreamHttpEndpoint = 'http://localhost:19090/ibe/inbound'
$DownstreamWebSocketPrefix = 'http://localhost:19091/ibe/ws/'
$DownstreamWebSocketEndpoint = 'ws://localhost:19091/ibe/ws/'
$DownstreamTcpMarker = 'TCP-DOWNSTREAM-OK'
$DownstreamHttpMarker = 'HTTP-DOWNSTREAM-OK'
$DownstreamWebSocketMarker = 'WS-DOWNSTREAM-OK'

$TcpOutputId = 100
$HttpOutputId = 200
$WebSocketOutputId = 300

function Log {
    param([string]$Message, [string]$Level = 'INFO')
    Write-HarnessLog -Level $Level -Component 'workflow' -Message $Message -LogFile $WorkflowLog
}

# --- Contract generation -----------------------------------------------------
function New-ContractData {
    param([hashtable]$Scenario, [int]$Index)

    $inputId = $Index
    $endpoints = @{}

    switch ($Scenario.Input) {
        'tcp' {
            $endpoints['TcpInbound'] = @(
                @{ SourceEndpointId = $inputId; Port = (16000 + $Index); Format = 'hl7v2' }
            )
        }
        'websocket' {
            $endpoints['WebSocketInbound'] = @(
                @{ SourceEndpointId = $inputId; Prefix = ('http://localhost:{0}/ibe/ws/' -f (19000 + $Index)); Format = 'hl7v2' }
            )
        }
        default {
            # A short reply timeout keeps the no-ack case fast (the request is held then
            # released with 504); ack/response modes need long enough to settle delivery.
            $replyTimeout = if ($Scenario.Ack -eq 'none') { '00:00:03' } else { '00:00:20' }
            $endpoints['HttpInbound'] = @(
                @{ SourceEndpointId = $inputId; Prefix = ('http://localhost:{0}/ibe/' -f (18000 + $Index)); Format = 'hl7v2'; ReplyTimeout = $replyTimeout }
            )
        }
    }

    $outputs = @()
    if ($Scenario.Outputs -contains 'tcp') {
        $endpoints['TcpOutbound'] = @(
            @{ OutputId = $TcpOutputId; Host = '127.0.0.1'; Port = $DownstreamTcpPort; ExpectReply = $true }
        )
        $outputs += @{ OutputId = $TcpOutputId; Encoding = 'hl7v2' }
    }
    if ($Scenario.Outputs -contains 'http') {
        $endpoints['HttpOutbound'] = @(
            @{ OutputId = $HttpOutputId; Endpoint = $DownstreamHttpEndpoint; ContentType = 'application/octet-stream'; TimeoutSeconds = 30 }
        )
        $outputs += @{ OutputId = $HttpOutputId; Encoding = 'hl7v2' }
    }
    if ($Scenario.Outputs -contains 'websocket') {
        $endpoints['WebSocketOutbound'] = @(
            @{ OutputId = $WebSocketOutputId; Endpoint = $DownstreamWebSocketEndpoint; ExpectReply = $true }
        )
        $outputs += @{ OutputId = $WebSocketOutputId; Encoding = 'hl7v2' }
    }

    $contract = @{
        Name    = $Scenario.Name
        Inputs  = @(@{ InputId = $inputId })
        Outputs = $outputs
    }
    # No Pipeline is set: these scenarios exercise transport and acknowledgement,
    # not processing stages, so each contract runs as a pass-through (the engine
    # treats a null Pipeline as "no processing stages").

    switch ($Scenario.Ack) {
        'none' {
            $contract['Acknowledgement'] = @{ IsEnabled = $false }
            $contract['Response'] = @{ IsEnabled = $false }
        }
        'normal' {
            $contract['Acknowledgement'] = @{ IsEnabled = $true; IsEnhanced = $false; Shape = 'Single' }
        }
        'enhanced' {
            $contract['Acknowledgement'] = @{ IsEnabled = $true; IsEnhanced = $true; Shape = 'Single' }
        }
        'response' {
            $contract['Acknowledgement'] = @{ IsEnabled = $false }
            $contract['Response'] = @{ IsEnabled = $true; TimeoutMs = 20000 }
        }
    }

    return @{ Endpoints = $endpoints; Contracts = @($contract) }
}

function New-Hl7Message {
    param([string]$MessageId, [int]$Index)
    $stamp = (Get-Date).ToString('yyyyMMddHHmmss')
    return (@(
            "MSH|^~\&|IBE_E2E|HARNESS|DOWNSTREAM|FACILITY|$stamp||ADT^A01|$MessageId|P|2.5"
            "EVN|A01|$stamp"
            "PID|1||E2E-$Index^^^HOSP^MR||DOE^JOHN^E"
            "ZMK|$MessageId"
        ) -join "`r")
}

# --- Agent lifecycle ---------------------------------------------------------
function Start-Agent {
    param([hashtable]$ContractData, [string]$OutLog, [string]$ErrLog)

    $json = $ContractData | ConvertTo-Json -Depth 12
    Set-Content -LiteralPath (Join-Path $AgentDir 'contractData.json') -Value $json -Encoding UTF8

    return Start-Process -FilePath $AgentExe -WorkingDirectory $AgentDir -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $OutLog -RedirectStandardError $ErrLog
}

function Stop-Agent {
    param($Process)
    if ($Process -and -not $Process.HasExited) {
        try { Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue } catch { }
        $null = $Process.WaitForExit(5000)
    }
}

# --- Scenario runner ---------------------------------------------------------
function Invoke-Scenario {
    param([hashtable]$Scenario, [int]$Index)

    $messageId = '{0}-S{1:D2}' -f $RunId, $Index
    $outputLabel = ($Scenario.Outputs -join ' + ')
    $agentOutLog = Join-Path $RunDir ('agent.S{0:D2}.out.log' -f $Index)
    $agentErrLog = Join-Path $RunDir ('agent.S{0:D2}.err.log' -f $Index)

    Log ("Scenario {0}: {1}" -f $Index, $Scenario.Name) 'STEP'
    Log ("  input: {0}   output: {1}   ack mode: {2}   message id: {3}" -f $Scenario.Input, $outputLabel, $Scenario.Ack, $messageId)

    $record = [ordered]@{
        Index         = $Index
        Name          = $Scenario.Name
        Input         = $Scenario.Input
        Output        = $outputLabel
        Ack           = $Scenario.Ack
        Delivered     = 'No'
        Reply         = ''
        ReplyExpected = ''
        LatencyMs     = $null
        Verdict       = 'FAIL'
        Detail        = ''
        AgentLog      = $agentOutLog
    }

    $agent = $null
    try {
        $contractData = New-ContractData -Scenario $Scenario -Index $Index
        $agent = Start-Agent -ContractData $contractData -OutLog $agentOutLog -ErrLog $agentErrLog

        $inboundPort = switch ($Scenario.Input) {
            'tcp' { 16000 + $Index }
            'websocket' { 19000 + $Index }
            default { 18000 + $Index }
        }
        $ready = $false
        $deadline = (Get-Date).AddSeconds(20)
        while ((Get-Date) -lt $deadline) {
            if ($agent.HasExited) { break }
            if (Wait-PortOpen -Port $inboundPort -TimeoutMs 500) { $ready = $true; break }
            Start-Sleep -Milliseconds 200
        }
        if (-not $ready) {
            $record.Verdict = 'ERROR'
            if ($agent.HasExited) {
                $errText = (Get-Content -LiteralPath $agentErrLog -Raw -ErrorAction SilentlyContinue)
                $firstError = if ($errText) { (($errText -split "`r?`n") | Where-Object { $_.Trim() } | Select-Object -First 2) -join ' ' } else { '(no error output captured)' }
                $record.Detail = "Agent exited during startup: $firstError"
            }
            else {
                $record.Detail = "Agent inbound endpoint on port $inboundPort did not become ready."
            }
            Log ("  " + $record.Detail) 'ERROR'
            return [pscustomobject]$record
        }
        Log ("  agent ready on inbound port {0}" -f $inboundPort)

        $message = New-Hl7Message -MessageId $messageId -Index $Index

        # ---- Send from the upstream comm point ----
        switch ($Scenario.Input) {
            'tcp' {
                $expectAck = $Scenario.Ack -ne 'none'
                $ackTimeout = if ($expectAck) { 15000 } else { 1500 }
                $reply = & $PeerTcpSender -Port $inboundPort -Payload $message -ExpectAck $expectAck -AckTimeoutMs $ackTimeout
            }
            'websocket' {
                $expectAck = $Scenario.Ack -ne 'none'
                $ackTimeout = if ($expectAck) { 15000 } else { 1500 }
                $uri = 'ws://localhost:{0}/ibe/ws/' -f $inboundPort
                $reply = & $PeerWebSocketSender -Uri $uri -Payload $message -ExpectAck $expectAck -AckTimeoutMs $ackTimeout
            }
            default {
                $uri = 'http://localhost:{0}/ibe/' -f $inboundPort
                $reply = & $PeerHttpSender -Uri $uri -Payload $message -TimeoutSec 40
            }
        }
        $record.LatencyMs = $reply.LatencyMs

        if ($reply.Error) {
            Log ("  upstream send reported: {0}" -f $reply.Error) 'WARN'
        }

        # ---- Confirm downstream delivery ----
        $deliveredAll = $true
        $deliveredDetail = @()
        foreach ($out in $Scenario.Outputs) {
            $captureFile = switch ($out) {
                'tcp' { $TcpCaptureFile }
                'websocket' { $WebSocketCaptureFile }
                default { $HttpCaptureFile }
            }
            $ok = Wait-ForCapture -Path $captureFile -Marker $messageId -TimeoutMs 8000
            if ($ok) {
                $deliveredDetail += "$out=delivered"
                Log ("  downstream {0} received the message" -f $out) 'INFO'
            }
            else {
                $deliveredAll = $false
                $deliveredDetail += "$out=missing"
                Log ("  downstream {0} did NOT receive the message" -f $out) 'WARN'
            }
        }
        $record.Delivered = if ($deliveredAll) { 'Yes' } else { 'No' }

        # ---- Evaluate the source-side reply for the mode ----
        $ackText = if ($reply.AckText) { [string]$reply.AckText } else { '' }
        $ackTextFlat = $ackText -replace "`r", ' ' -replace "`n", ' '
        $isHttp = $Scenario.Input -eq 'http'

        $replyPass = $false
        switch ($Scenario.Ack) {
            'none' {
                if ($isHttp) {
                    $record.ReplyExpected = 'HTTP 504 (held, then released)'
                    $replyPass = ($reply.StatusCode -eq 504)
                    $record.Reply = "HTTP $($reply.StatusCode)"
                }
                else {
                    $record.ReplyExpected = 'no acknowledgement'
                    $replyPass = (-not $reply.AckReceived)
                    $record.Reply = if ($reply.AckReceived) { "unexpected ack: $ackTextFlat" } else { 'none' }
                }
            }
            'normal' {
                $record.ReplyExpected = 'acknowledgement containing "received"'
                $got = $ackText.Contains('received')
                $replyPass = if ($isHttp) { ($reply.StatusCode -eq 200) -and $got } else { $reply.AckReceived -and $got }
                $record.Reply = if ($isHttp) { "HTTP $($reply.StatusCode): $ackTextFlat" } else { $ackTextFlat }
            }
            'enhanced' {
                $record.ReplyExpected = "downstream acknowledgement relayed end-to-end (contains 'DOWNSTREAM-OK')"
                $got = $ackText.Contains('DOWNSTREAM-OK')
                $replyPass = if ($isHttp) { ($reply.StatusCode -eq 200) -and $got } else { $reply.AckReceived -and $got }
                $record.Reply = if ($isHttp) { "HTTP $($reply.StatusCode): $ackTextFlat" } else { $ackTextFlat }
            }
            'response' {
                $marker = if ($Scenario.Outputs -contains 'tcp') { $DownstreamTcpMarker }
                    elseif ($Scenario.Outputs -contains 'websocket') { $DownstreamWebSocketMarker }
                    else { $DownstreamHttpMarker }
                $record.ReplyExpected = "downstream response containing '$marker'"
                $got = $ackText.Contains($marker)
                $replyPass = if ($isHttp) { ($reply.StatusCode -eq 200) -and $got } else { $reply.AckReceived -and $got }
                $record.Reply = if ($isHttp) { "HTTP $($reply.StatusCode): $ackTextFlat" } else { $ackTextFlat }
            }
        }

        if ($deliveredAll -and $replyPass) {
            $record.Verdict = 'PASS'
            $record.Detail = ($deliveredDetail -join ', ')
            Log ("  verdict: PASS  (delivery: {0}; reply: {1})" -f ($deliveredDetail -join ', '), $record.Reply) 'PASS'
        }
        else {
            $record.Verdict = 'FAIL'
            $reasons = @()
            if (-not $deliveredAll) { $reasons += "delivery incomplete ($($deliveredDetail -join ', '))" }
            if (-not $replyPass) { $reasons += "reply mismatch (expected $($record.ReplyExpected), got '$($record.Reply)')" }
            $record.Detail = ($reasons -join '; ')
            Log ("  verdict: FAIL  ({0})" -f $record.Detail) 'FAIL'
        }
    }
    catch {
        $record.Verdict = 'ERROR'
        $record.Detail = $_.Exception.Message
        Log ("  scenario raised an error: {0}" -f $_.Exception.Message) 'ERROR'
    }
    finally {
        Stop-Agent -Process $agent
    }

    return [pscustomobject]$record
}

# --- Downstream comm point lifecycle -----------------------------------------
function Start-DownstreamReceivers {
    if (Test-Path -LiteralPath $StopFile) { Remove-Item -LiteralPath $StopFile -Force }

    Log 'Starting downstream comm points (delivery targets).' 'STEP'
    $tcp = Start-Process -FilePath $PwshExe -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PeerTcpReceiver,
        '-Port', $DownstreamTcpPort,
        '-CaptureFile', $TcpCaptureFile,
        '-LogFile', $TcpReceiverLog,
        '-StopFile', $StopFile,
        '-ReplyPayload', "MSA|AA|$DownstreamTcpMarker"
    )
    $http = Start-Process -FilePath $PwshExe -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PeerHttpReceiver,
        '-Prefix', $DownstreamHttpPrefix,
        '-CaptureFile', $HttpCaptureFile,
        '-LogFile', $HttpReceiverLog,
        '-StopFile', $StopFile,
        '-ResponseBody', "MSA|AA|$DownstreamHttpMarker"
    )
    $websocket = Start-Process -FilePath $PwshExe -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PeerWebSocketReceiver,
        '-Prefix', $DownstreamWebSocketPrefix,
        '-CaptureFile', $WebSocketCaptureFile,
        '-LogFile', $WebSocketReceiverLog,
        '-StopFile', $StopFile,
        '-ReplyPayload', "MSA|AA|$DownstreamWebSocketMarker"
    )

    if (-not (Wait-PortOpen -Port $DownstreamTcpPort -TimeoutMs 10000)) {
        throw "Downstream TCP receiver did not open port $DownstreamTcpPort."
    }
    if (-not (Wait-PortOpen -Port 19090 -TimeoutMs 10000)) {
        throw 'Downstream HTTP receiver did not open port 19090.'
    }
    if (-not (Wait-PortOpen -Port 19091 -TimeoutMs 10000)) {
        throw 'Downstream WebSocket receiver did not open port 19091.'
    }
    Log ("  TCP delivery target listening on 127.0.0.1:{0}" -f $DownstreamTcpPort)
    Log ("  HTTP delivery target listening on {0}" -f $DownstreamHttpPrefix)
    Log ("  WebSocket delivery target listening on {0}" -f $DownstreamWebSocketPrefix)

    return @{ Tcp = $tcp; Http = $http; WebSocket = $websocket }
}

function Stop-DownstreamReceivers {
    param($Receivers)
    Log 'Stopping downstream comm points.' 'STEP'
    New-Item -ItemType File -Path $StopFile -Force | Out-Null
    Start-Sleep -Milliseconds 500
    foreach ($proc in @($Receivers.Tcp, $Receivers.Http, $Receivers.WebSocket)) {
        if ($proc -and -not $proc.HasExited) {
            try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch { }
        }
    }
    if (Test-Path -LiteralPath $StopFile) { Remove-Item -LiteralPath $StopFile -Force -ErrorAction SilentlyContinue }
}

# --- Main --------------------------------------------------------------------
Log '=================================================================='
Log 'IBE Agent end-to-end test workflow'
Log ("Run id: {0}" -f $RunId)
Log ("Artifacts: {0}" -f $RunDir)
Log '=================================================================='

# Publish the agent under test.
if ($SkipBuild -and (Test-Path -LiteralPath $AgentExe)) {
    Log 'Reusing the previously published agent (-SkipBuild).' 'STEP'
}
else {
    Log ("Publishing the IBE Agent host ({0})." -f $Configuration) 'STEP'
    & dotnet publish $ServiceCsproj -c $Configuration -o $AgentDir --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Log 'Agent publish failed. Aborting.' 'ERROR'
        exit 1
    }
    Log ("  published to {0}" -f $AgentDir)
}

# Load and filter scenarios.
$scenarios = (Import-PowerShellDataFile -Path (Join-Path $E2eRoot 'scenarios.psd1')).Scenarios
if ($Only) {
    $scenarios = $scenarios | Where-Object {
        $name = $_.Name
        @($Only | Where-Object { $name -like $_ }).Count -gt 0
    }
}
Log ("Selected {0} scenario(s)." -f @($scenarios).Count) 'STEP'

$results = @()
$receivers = $null
try {
    $receivers = Start-DownstreamReceivers

    $index = 0
    foreach ($scenario in $scenarios) {
        $index++
        Log '------------------------------------------------------------------'
        $results += Invoke-Scenario -Scenario $scenario -Index $index
    }
}
finally {
    if ($receivers) { Stop-DownstreamReceivers -Receivers $receivers }
}

# --- Report ------------------------------------------------------------------
$passCount = @($results | Where-Object { $_.Verdict -eq 'PASS' }).Count
$failCount = @($results | Where-Object { $_.Verdict -eq 'FAIL' }).Count
$errorCount = @($results | Where-Object { $_.Verdict -eq 'ERROR' }).Count

$summaryTable = $results |
    Select-Object Index,
        @{ Name = 'Scenario'; Expression = { $_.Name } },
        @{ Name = 'Ack'; Expression = { $_.Ack } },
        @{ Name = 'Delivered'; Expression = { $_.Delivered } },
        @{ Name = 'Verdict'; Expression = { $_.Verdict } } |
    Format-Table -AutoSize | Out-String

Log '=================================================================='
Log 'Summary'
Log '=================================================================='
foreach ($line in ($summaryTable -split "`r?`n")) {
    if ($line.Trim()) { Log $line }
}
Log ("Total: {0}    Passed: {1}    Failed: {2}    Errors: {3}" -f @($results).Count, $passCount, $failCount, $errorCount)

# Persist machine-readable and human-readable reports.
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $RunDir 'report.json') -Encoding UTF8

$reportText = @()
$reportText += 'IBE Agent end-to-end test report'
$reportText += "Run id: $RunId"
$reportText += "Generated: $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))"
$reportText += ''
$reportText += "Total: $(@($results).Count)    Passed: $passCount    Failed: $failCount    Errors: $errorCount"
$reportText += ''
foreach ($r in $results) {
    $reportText += "[$($r.Verdict)] Scenario $($r.Index): $($r.Name)"
    $reportText += "    input=$($r.Input)  output=$($r.Output)  ack=$($r.Ack)  latency=$($r.LatencyMs)ms"
    $reportText += "    delivered=$($r.Delivered)  reply=$($r.Reply)"
    $reportText += "    expected reply: $($r.ReplyExpected)"
    if ($r.Detail) { $reportText += "    detail: $($r.Detail)" }
    $reportText += "    agent log: $($r.AgentLog)"
    $reportText += ''
}
$reportText -join [Environment]::NewLine | Set-Content -LiteralPath (Join-Path $RunDir 'report.txt') -Encoding UTF8

Log ("Reports written to {0}" -f $RunDir)

if ($failCount -gt 0 -or $errorCount -gt 0) { exit 1 } else { exit 0 }
