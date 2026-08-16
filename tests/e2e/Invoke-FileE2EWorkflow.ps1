<#
.SYNOPSIS
    End-to-end test workflow for the IBE Agent File comm points.

.DESCRIPTION
    Companion to Invoke-E2EWorkflow.ps1 (TCP/HTTP). Exercises the File inbound
    (folder poller) and File outbound (file writer) comm points against a real
    published agent, including cross-transport relays (File <-> TCP/HTTP), the
    three input dispositions (Move -> processed/, Watermark), the
    error path (Move -> error/ on a failed delivery), and the content path
    (base64 payload codec + blob-envelope-extract pipeline).

    A File source has no reply channel, so the source-side assertion is the
    DISPOSITION of the input file on disk, not a reply on a socket. File
    deliveries are verified by polling the output directory. The existing TCP
    and HTTP downstream/upstream peers are reused for cross-transport legs.

    For each scenario the workflow generates a contractData.json (and, once, an
    augmented catalogData.json that adds the base64 codec and blob pipeline) in
    the .agent sandbox, starts a fresh agent, drives one uniquely marked message
    through it, and records delivery + disposition. Evidence lands under
    artifacts/file-<run-id>/.

.PARAMETER Only
    One or more wildcard patterns; only scenarios whose name matches are run.

.PARAMETER Configuration
    Build configuration for the agent (Debug or Release). Default: Debug.

.PARAMETER SkipBuild
    Reuse the previously published agent instead of publishing again.

.EXAMPLE
    pwsh -File tests/e2e/Invoke-FileE2EWorkflow.ps1

.EXAMPLE
    pwsh -File tests/e2e/Invoke-FileE2EWorkflow.ps1 -Only '*Watermark*'
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
. (Join-Path $E2eRoot 'lib\FileCommon.ps1')

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
$PeerTcpSender = Join-Path $E2eRoot 'peers\Send-TcpMessage.ps1'
$PeerHttpSender = Join-Path $E2eRoot 'peers\Send-HttpMessage.ps1'
$PeerFileSender = Join-Path $E2eRoot 'peers\Send-FileMessage.ps1'

$RunId = (Get-Date).ToString('yyyyMMdd-HHmmss')
$RunDir = Join-Path $E2eRoot "artifacts\file-$RunId"
New-Item -ItemType Directory -Path $RunDir -Force | Out-Null

$WorkflowLog = Join-Path $RunDir 'workflow.log'
$TcpCaptureFile = Join-Path $RunDir 'downstream-tcp.capture.jsonl'
$HttpCaptureFile = Join-Path $RunDir 'downstream-http.capture.jsonl'
$TcpReceiverLog = Join-Path $RunDir 'downstream-tcp.log'
$HttpReceiverLog = Join-Path $RunDir 'downstream-http.log'
$StopFile = Join-Path $RunDir 'STOP'

# --- Fixed topology ----------------------------------------------------------
$DownstreamTcpPort = 17001
$DownstreamHttpPrefix = 'http://localhost:19090/ibe/'
$DownstreamHttpEndpoint = 'http://localhost:19090/ibe/inbound'
$DeadTcpPort = 17099          # deliberately has no listener -> forces a delivery failure

$TcpOutputId = 100
$HttpOutputId = 200
$FileOutputId = 300

function Log {
    param([string]$Message, [string]$Level = 'INFO')
    Write-HarnessLog -Level $Level -Component 'file-workflow' -Message $Message -LogFile $WorkflowLog
}

function Get-Field {
    param([hashtable]$H, [string]$Key, $Default = $null)
    if ($H.ContainsKey($Key)) { return $H[$Key] }
    return $Default
}

# --- Message + contract generation -------------------------------------------
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

# Builds the payload to inject and what to look for downstream. The delivered
# marker is always the message id; for the envelope case it also drives the
# expected output file name (blob.name).
function New-ScenarioMessage {
    param([hashtable]$Scenario, [string]$MessageId, [int]$Index)

    $content = Get-Field $Scenario 'Content' 'plain'
    $hl7 = New-Hl7Message -MessageId $MessageId -Index $Index

    switch ($content) {
        'envelope' {
            $decoded = "BLOB-CONTENT $MessageId"
            $b64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($decoded))
            $outName = "blob-$MessageId.dat"
            $envelope = @{ filename = $outName; destinationpath = 'ignored-subfolder'; filecontent = $b64 } | ConvertTo-Json -Compress
            return @{ Payload = $envelope; Extension = 'hl7'; DeliveredMarker = $MessageId; ExpectedOutputName = $outName }
        }
        'base64' {
            $b64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($hl7))
            return @{ Payload = $b64; Extension = 'hl7'; DeliveredMarker = $MessageId; ExpectedOutputName = $null }
        }
        default {
            return @{ Payload = $hl7; Extension = 'hl7'; DeliveredMarker = $MessageId; ExpectedOutputName = $null }
        }
    }
}

function New-FileContractData {
    param([hashtable]$Scenario, [int]$Index, [string]$InDir, [string]$OutDir)

    $inputId = $Index
    $endpoints = @{}
    $disposition = Get-Field $Scenario 'Disposition' 'Move'
    $content = Get-Field $Scenario 'Content' 'plain'

    switch ($Scenario.Input) {
        'file' {
            $endpoints['FileInbound'] = @(
                @{ SourceEndpointId = $inputId; Directory = $InDir; FilePattern = '*.hl7;*.dat;*.txt'; PollIntervalSeconds = 1; Format = 'hl7v2'; KeepOriginalFiles = ($disposition -eq 'Watermark') }
            )
        }
        'tcp' {
            $endpoints['TcpInbound'] = @(@{ SourceEndpointId = $inputId; Port = (16000 + $Index); Format = 'hl7v2' })
        }
        'http' {
            $replyTimeout = if ($Scenario.Ack -eq 'none') { '00:00:03' } else { '00:00:20' }
            $endpoints['HttpInbound'] = @(@{ SourceEndpointId = $inputId; Prefix = ('http://localhost:{0}/ibe/' -f (18000 + $Index)); Format = 'hl7v2'; ReplyTimeout = $replyTimeout })
        }
    }

    # 'hl7v2' is a pass-through codec (raw bytes); 'base64' decodes the payload.
    # For the envelope case the blob pipeline already decoded the payload, so the
    # output leg writes it raw via the pass-through codec.
    $outEncoding = if ($content -eq 'base64') { 'base64' } else { 'hl7v2' }

    $outputs = @()
    switch ($Scenario.Output) {
        'file' {
            $endpoints['FileOutbound'] = @(@{ OutputId = $FileOutputId; Directory = $OutDir; DefaultExtension = 'dat' })
            $outputs += @{ OutputId = $FileOutputId; Encoding = $outEncoding }
        }
        'tcp' {
            $port = if ((Get-Field $Scenario 'Dead' $false)) { $DeadTcpPort } else { $DownstreamTcpPort }
            $endpoints['TcpOutbound'] = @(@{ OutputId = $TcpOutputId; Host = '127.0.0.1'; Port = $port; ExpectReply = $true })
            $outputs += @{ OutputId = $TcpOutputId; Encoding = 'hl7v2' }
        }
        'http' {
            $endpoints['HttpOutbound'] = @(@{ OutputId = $HttpOutputId; Endpoint = $DownstreamHttpEndpoint; ContentType = 'application/octet-stream'; TimeoutSeconds = 30 })
            $outputs += @{ OutputId = $HttpOutputId; Encoding = 'hl7v2' }
        }
    }

    $contract = @{
        Name    = ('S{0:D2}' -f $Index)
        Inputs  = @(@{ InputId = $inputId })
        Outputs = $outputs
    }
    if ($content -eq 'envelope') { $contract['Pipeline'] = 'blob' }

    switch ($Scenario.Ack) {
        'none' {
            $contract['Acknowledgement'] = @{ IsEnabled = $false }
            $contract['Response'] = @{ IsEnabled = $false }
        }
        'normal' {
            $contract['Acknowledgement'] = @{ IsEnabled = $true; IsEnhanced = $false; Shape = 'Single' }
        }
        'enhanced' {
            $contract['Acknowledgement'] = @{ IsEnabled = $true; IsEnhanced = $true; Shape = 'Single'; TimeoutMs = 6000 }
        }
    }

    return @{ Endpoints = $endpoints; Contracts = @($contract) }
}

# Augmented catalog written once into the sandbox: adds the base64 codec and the
# blob-envelope-extract pipeline on top of the shipped hl7v2 / passthrough set.
function Write-AgentCatalog {
    $catalog = @{
        Catalog = @{
            Codecs    = @{ hl7v2 = @{ Type = 'hl7v2' }; base64 = @{ Type = 'base64' } }
            Pipelines = @{ main = @('passthrough'); blob = @('blob-envelope-extract') }
        }
    }
    Set-Content -LiteralPath (Join-Path $AgentDir 'catalogData.json') -Value ($catalog | ConvertTo-Json -Depth 8) -Encoding UTF8
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

# Waits until the agent's inbound comm point is ready. TCP/HTTP expose a port;
# a File poller does not, so its readiness is taken from its "polling" log line.
function Wait-InputReady {
    param([hashtable]$Scenario, [int]$Index, $Agent, [string]$OutLog)
    switch ($Scenario.Input) {
        'file' { return (Wait-ForAgentLog -LogFile $OutLog -Pattern 'polling' -TimeoutMs 20000) }
        'tcp' { return (Wait-PortOpen -Port (16000 + $Index) -TimeoutMs 20000) }
        'http' { return (Wait-PortOpen -Port (18000 + $Index) -TimeoutMs 20000) }
    }
    return $false
}

# --- Scenario runner ---------------------------------------------------------
function Invoke-Scenario {
    param([hashtable]$Scenario, [int]$Index)

    $messageId = '{0}-S{1:D2}' -f $RunId, $Index
    $agentOutLog = Join-Path $RunDir ('agent.S{0:D2}.out.log' -f $Index)
    $agentErrLog = Join-Path $RunDir ('agent.S{0:D2}.err.log' -f $Index)

    $scenarioDir = Join-Path $RunDir ('s{0:D2}' -f $Index)
    $inDir = Join-Path $scenarioDir 'in'
    $outDir = Join-Path $scenarioDir 'out'
    if ($Scenario.Input -eq 'file') { New-Item -ItemType Directory -Path $inDir -Force | Out-Null }
    if ($Scenario.Output -eq 'file') { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

    $disposition = Get-Field $Scenario 'Disposition' 'Move'
    $expectError = [bool](Get-Field $Scenario 'Dead' $false)

    Log ("Scenario {0}: {1}" -f $Index, $Scenario.Name) 'STEP'
    Log ("  input: {0}   output: {1}   ack: {2}   disposition: {3}   id: {4}" -f $Scenario.Input, $Scenario.Output, $Scenario.Ack, $disposition, $messageId)

    $record = [ordered]@{
        Index       = $Index
        Name        = $Scenario.Name
        Input       = $Scenario.Input
        Output      = $Scenario.Output
        Ack         = $Scenario.Ack
        Disposition = if ($Scenario.Input -eq 'file') { $disposition } else { 'n/a' }
        Delivered   = 'No'
        Source      = ''
        Verdict     = 'FAIL'
        Detail      = ''
        AgentLog    = $agentOutLog
    }

    $agent = $null
    try {
        $contractData = New-FileContractData -Scenario $Scenario -Index $Index -InDir $inDir -OutDir $outDir
        $agent = Start-Agent -ContractData $contractData -OutLog $agentOutLog -ErrLog $agentErrLog

        if (-not (Wait-InputReady -Scenario $Scenario -Index $Index -Agent $agent -OutLog $agentOutLog)) {
            $record.Verdict = 'ERROR'
            if ($agent.HasExited) {
                $errText = (Get-Content -LiteralPath $agentErrLog -Raw -ErrorAction SilentlyContinue)
                $firstError = if ($errText) { (($errText -split "`r?`n") | Where-Object { $_.Trim() } | Select-Object -First 2) -join ' ' } else { '(no error output captured)' }
                $record.Detail = "Agent exited during startup: $firstError"
            }
            else {
                $record.Detail = "Agent inbound ($($Scenario.Input)) did not become ready."
            }
            Log ("  " + $record.Detail) 'ERROR'
            return [pscustomobject]$record
        }
        Log '  agent inbound ready'

        $msg = New-ScenarioMessage -Scenario $Scenario -MessageId $messageId -Index $Index

        # ---- Inject from the upstream comm point ----
        switch ($Scenario.Input) {
            'file' {
                $dropName = 'msg-{0}.{1}' -f $messageId, $msg.Extension   # message id in the name -> disposition is matchable on disk
                $drop = & $PeerFileSender -TargetDir $inDir -Payload $msg.Payload -FileName $dropName
                if (-not $drop.Sent) { throw "File drop failed: $($drop.Error)" }
                Log ("  dropped {0}" -f (Split-Path $drop.Path -Leaf))
            }
            'tcp' {
                $null = & $PeerTcpSender -Port (16000 + $Index) -Payload $msg.Payload -ExpectAck $false -AckTimeoutMs 1500
                Log '  sent via TCP inbound'
            }
            'http' {
                $null = & $PeerHttpSender -Uri ('http://localhost:{0}/ibe/' -f (18000 + $Index)) -Payload $msg.Payload -TimeoutSec 15
                Log '  sent via HTTP inbound'
            }
        }

        # ---- Confirm (or, for the dead-downstream case, refute) delivery ----
        $delivered = $false
        $deliveredDetail = ''
        switch ($Scenario.Output) {
            'file' {
                $hit = Wait-ForOutputFile -Directory $outDir -Marker $msg.DeliveredMarker -TimeoutMs 15000
                $delivered = $hit.Found
                if ($delivered -and $msg.ExpectedOutputName) {
                    if ($hit.Name -ne $msg.ExpectedOutputName) {
                        $delivered = $false
                        $deliveredDetail = "output name '$($hit.Name)' != expected '$($msg.ExpectedOutputName)'"
                    }
                    else { $deliveredDetail = "file '$($hit.Name)'" }
                }
                elseif ($delivered) { $deliveredDetail = "file '$($hit.Name)'" }
            }
            'tcp' {
                $timeout = if ($expectError) { 4000 } else { 15000 }
                $delivered = Wait-ForCapture -Path $TcpCaptureFile -Marker $msg.DeliveredMarker -TimeoutMs $timeout
            }
            'http' {
                $delivered = Wait-ForCapture -Path $HttpCaptureFile -Marker $msg.DeliveredMarker -TimeoutMs 15000
            }
        }
        $record.Delivered = if ($delivered) { 'Yes' } else { 'No' }

        # ---- Evaluate the source-side outcome ----
        $sourceOk = $false
        if ($Scenario.Input -eq 'file') {
            if ($expectError) {
                $disp = Wait-ForDisposition -InputDir $inDir -Marker $messageId -Outcome 'error' -TimeoutMs 15000
                $sourceOk = $disp.Found
                $record.Source = if ($sourceOk) { 'moved to error/' } else { 'not in error/' }
            }
            elseif ($disposition -eq 'Watermark') {
                Start-Sleep -Milliseconds 2500   # give the poller >2 intervals to (not) re-read
                $stillThere = Test-InputHasMarker -InputDir $inDir -Marker $messageId
                $count = (Get-OutputFilesWithMarker -Directory $outDir -Marker $msg.DeliveredMarker).Count
                $watermarkFile = Test-Path -LiteralPath (Join-Path $inDir '.lastProcessedTime')
                $sourceOk = $stillThere -and ($count -eq 1) -and $watermarkFile
                $record.Source = "left in place=$stillThere, deliveries=$count, watermark=$watermarkFile"
            }
            else {
                $disp = Wait-ForDisposition -InputDir $inDir -Marker $messageId -Outcome 'processed' -TimeoutMs 15000
                $sourceOk = $disp.Found
                $record.Source = if ($sourceOk) { 'moved to processed/' } else { 'not in processed/' }
            }
        }
        else {
            # TCP/HTTP source-side replies are covered by the sibling harness; here
            # the send succeeding is enough (the File-output delivery is the focus).
            $sourceOk = $true
            $record.Source = 'sent'
        }

        # ---- Verdict ----
        $deliveryOk = if ($expectError) { -not $delivered } else { $delivered }
        if ($deliveryOk -and $sourceOk) {
            $record.Verdict = 'PASS'
            $record.Detail = (@($deliveredDetail, $record.Source) | Where-Object { $_ }) -join '; '
            Log ("  verdict: PASS  (delivered: {0}; source: {1})" -f $record.Delivered, $record.Source) 'PASS'
        }
        else {
            $reasons = @()
            if (-not $deliveryOk) { $reasons += if ($expectError) { 'message was delivered but a failure was expected' } else { "delivery not observed$(if ($deliveredDetail) { " ($deliveredDetail)" })" } }
            if (-not $sourceOk) { $reasons += "source outcome wrong ($($record.Source))" }
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

# --- Downstream comm point lifecycle (reused TCP/HTTP peers) ------------------
function Start-DownstreamReceivers {
    if (Test-Path -LiteralPath $StopFile) { Remove-Item -LiteralPath $StopFile -Force }
    Log 'Starting downstream TCP/HTTP comm points (for cross-transport legs).' 'STEP'
    $tcp = Start-Process -FilePath $PwshExe -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PeerTcpReceiver,
        '-Port', $DownstreamTcpPort, '-CaptureFile', $TcpCaptureFile, '-LogFile', $TcpReceiverLog,
        '-StopFile', $StopFile, '-ReplyPayload', 'MSA|AA|TCP-DOWNSTREAM-OK'
    )
    $http = Start-Process -FilePath $PwshExe -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PeerHttpReceiver,
        '-Prefix', $DownstreamHttpPrefix, '-CaptureFile', $HttpCaptureFile, '-LogFile', $HttpReceiverLog,
        '-StopFile', $StopFile, '-ResponseBody', 'MSA|AA|HTTP-DOWNSTREAM-OK'
    )
    if (-not (Wait-PortOpen -Port $DownstreamTcpPort -TimeoutMs 10000)) { throw "Downstream TCP receiver did not open port $DownstreamTcpPort." }
    if (-not (Wait-PortOpen -Port 19090 -TimeoutMs 10000)) { throw 'Downstream HTTP receiver did not open port 19090.' }
    Log ("  TCP delivery target on 127.0.0.1:{0}; HTTP on {1}" -f $DownstreamTcpPort, $DownstreamHttpPrefix)
    return @{ Tcp = $tcp; Http = $http }
}

function Stop-DownstreamReceivers {
    param($Receivers)
    Log 'Stopping downstream comm points.' 'STEP'
    New-Item -ItemType File -Path $StopFile -Force | Out-Null
    Start-Sleep -Milliseconds 500
    foreach ($proc in @($Receivers.Tcp, $Receivers.Http)) {
        if ($proc -and -not $proc.HasExited) { try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch { } }
    }
    if (Test-Path -LiteralPath $StopFile) { Remove-Item -LiteralPath $StopFile -Force -ErrorAction SilentlyContinue }
}

# --- Main --------------------------------------------------------------------
Log '=================================================================='
Log 'IBE Agent File comm-point end-to-end test workflow'
Log ("Run id: {0}" -f $RunId)
Log ("Artifacts: {0}" -f $RunDir)
Log '=================================================================='

if ($SkipBuild -and (Test-Path -LiteralPath $AgentExe)) {
    Log 'Reusing the previously published agent (-SkipBuild).' 'STEP'
}
else {
    Log ("Publishing the IBE Agent host ({0})." -f $Configuration) 'STEP'
    & dotnet publish $ServiceCsproj -c $Configuration -o $AgentDir --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { Log 'Agent publish failed. Aborting.' 'ERROR'; exit 1 }
    Log ("  published to {0}" -f $AgentDir)
}

Write-AgentCatalog
Log '  wrote augmented catalogData.json (base64 codec + blob pipeline) to the sandbox.'

$scenarios = (Import-PowerShellDataFile -Path (Join-Path $E2eRoot 'file-scenarios.psd1')).Scenarios
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
        @{ Name = 'Disposition'; Expression = { $_.Disposition } },
        @{ Name = 'Delivered'; Expression = { $_.Delivered } },
        @{ Name = 'Verdict'; Expression = { $_.Verdict } } |
    Format-Table -AutoSize | Out-String

Log '=================================================================='
Log 'Summary'
Log '=================================================================='
foreach ($line in ($summaryTable -split "`r?`n")) { if ($line.Trim()) { Log $line } }
Log ("Total: {0}    Passed: {1}    Failed: {2}    Errors: {3}" -f @($results).Count, $passCount, $failCount, $errorCount)

$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $RunDir 'report.json') -Encoding UTF8

$reportText = @()
$reportText += 'IBE Agent File comm-point end-to-end test report'
$reportText += "Run id: $RunId"
$reportText += "Generated: $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))"
$reportText += ''
$reportText += ($summaryTable.TrimEnd())
$reportText += ''
foreach ($r in $results) {
    $reportText += ("[{0}] {1}" -f $r.Verdict, $r.Name)
    $reportText += ("    input={0} output={1} ack={2} disposition={3}" -f $r.Input, $r.Output, $r.Ack, $r.Disposition)
    $reportText += ("    delivered={0}  source={1}" -f $r.Delivered, $r.Source)
    if ($r.Detail) { $reportText += ("    detail: {0}" -f $r.Detail) }
}
$reportText += ''
$reportText += ("Total: {0}    Passed: {1}    Failed: {2}    Errors: {3}" -f @($results).Count, $passCount, $failCount, $errorCount)
Set-Content -LiteralPath (Join-Path $RunDir 'report.txt') -Value ($reportText -join [Environment]::NewLine) -Encoding UTF8

Log ("Report written to {0}" -f (Join-Path $RunDir 'report.txt'))

if ($failCount -eq 0 -and $errorCount -eq 0) { exit 0 } else { exit 1 }
