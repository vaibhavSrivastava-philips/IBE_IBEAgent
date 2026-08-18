<#
.SYNOPSIS
  One-shot performance suite for the IBE Agent: build (Release, Server-GC knob) -> run a scenario
  matrix against compiled peers -> emit a self-contained HTML report + per-scenario agent logs.

.DESCRIPTION
  Reads perf.config.json for the knobs + scenario matrix, and the LIVE config/contractData.json for
  topology (ports/protocols/contracts) so nothing is hard-coded. Runs in two modes, auto-detected:
    * dev mode    - publishes the agent + IbePerf from source in this repo.
    * bundle mode - uses the pre-published ./agent and ./tools/IbePerf next to this script
                    (produced by Export-PerfBundle.ps1) so it runs on a server with no source/SDK.

.EXAMPLE
  pwsh -File perf/Invoke-PerfSuite.ps1
  pwsh -File perf/Invoke-PerfSuite.ps1 -Scenario baseline -SkipBuild
  pwsh -File perf/Invoke-PerfSuite.ps1 -Baseline perf/results/20260807-120000
#>
[CmdletBinding()]
param(
    [string]$Config = "$PSScriptRoot/perf.config.json",
    [string]$Scenario = '',
    [switch]$SkipBuild,
    [string]$Baseline = '',
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/Common.ps1')

$cfg = ConvertTo-HashtableDeep (Get-Content $Config -Raw | ConvertFrom-Json)
$serverGc = [bool]$cfg.build.serverGc
$serverGcMode = [string]$cfg.build.serverGcMode

# ---- Mode + path resolution --------------------------------------------------------------------
$bundleAgentDir = Join-Path $PSScriptRoot 'agent'
$isBundle = Test-Path (Join-Path $bundleAgentDir $cfg.agent.exeName)

if ($isBundle) {
    $configDir = Join-Path $PSScriptRoot 'config'
    $agentDir  = $bundleAgentDir
    $toolExe   = Join-Path $PSScriptRoot 'tools/IbePerf/IbePerf.exe'
    $agentExe  = Join-Path $agentDir $cfg.agent.exeName
    Write-Host "Mode: BUNDLE (pre-published agent + tool)" -ForegroundColor Cyan
} else {
    $repoRoot  = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $configDir = Join-Path $repoRoot $cfg.agent.configDir
    $workDir   = Join-Path $PSScriptRoot '.agent'
    $agentDir  = Join-Path $workDir 'agent'
    $toolDir   = Join-Path $workDir 'tools'
    $toolExe   = Join-Path $toolDir 'IbePerf.exe'
    $agentExe  = Join-Path $agentDir $cfg.agent.exeName
    Write-Host "Mode: DEV (build from source)" -ForegroundColor Cyan
}

$contractPath = Join-Path $configDir 'contractData.json'
if (-not (Test-Path $contractPath)) { throw "contractData.json not found at $contractPath" }
$contractName = (Get-Content $contractPath -Raw | ConvertFrom-Json).Contracts[0].Name

# ---- Session folder ----------------------------------------------------------------------------
$sessionDir = Join-Path (Join-Path $PSScriptRoot 'results') (Get-Date -Format 'yyyyMMdd-HHmmss')
New-Item -ItemType Directory -Force -Path $sessionDir | Out-Null

Write-Host "Config      : $Config"
Write-Host "Contract    : $contractName ($contractPath)"
Write-Host "Server GC   : $serverGc ($serverGcMode)"
Write-Host "Session     : $sessionDir"
$scenarios = @($cfg.scenarios | Where-Object { -not $Scenario -or $_.name -eq $Scenario })
Write-Host "Scenarios   : $($scenarios.name -join ', ')"

if ($DryRun) { Write-Host "`n-DryRun: resolved plan only; exiting." -ForegroundColor Yellow; return }

# ---- Build (dev mode) --------------------------------------------------------------------------
if (-not $isBundle -and -not $SkipBuild -and $cfg.build.rebuild) {
    Stop-AgentProcesses
    $gcArgs = @()
    if ($serverGcMode -eq 'publish') {
        $gcArgs += "-p:ServerGarbageCollection=$($serverGc.ToString().ToLower())"
        $gcArgs += "-p:ConcurrentGarbageCollection=$($cfg.build.concurrentGc.ToString().ToLower())"
    }
    Write-Host "`nPublishing agent ($($cfg.build.configuration))..." -ForegroundColor Cyan
    & dotnet publish (Join-Path $repoRoot $cfg.agent.projectPath) -c $cfg.build.configuration -o $agentDir --nologo @gcArgs
    if ($LASTEXITCODE -ne 0) { throw 'agent publish failed' }
    Write-Host "Publishing IbePerf tool..." -ForegroundColor Cyan
    & dotnet publish (Join-Path $repoRoot 'perf/tools/IbePerf/IbePerf.csproj') -c Release -o $toolDir --nologo
    if ($LASTEXITCODE -ne 0) { throw 'IbePerf publish failed' }
}
if (-not (Test-Path $agentExe)) { throw "agent exe not found at $agentExe (build first, or run without -SkipBuild)" }
if (-not (Test-Path $toolExe))  { throw "IbePerf not found at $toolExe" }

# ---- sysinfo + slo -----------------------------------------------------------------------------
Get-SysInfo -ServerGc $serverGc -ContractName $contractName | ConvertTo-Json -Depth 4 |
    Set-Content (Join-Path $sessionDir 'sysinfo.json') -Encoding UTF8
$cfg.slo | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $sessionDir 'slo.json') -Encoding UTF8

$inboundPort = Get-InboundPort $contractPath

# ---- Scenario loop -----------------------------------------------------------------------------
foreach ($sc in $scenarios) {
    $merged = Merge-Config -Base (ConvertTo-HashtableDeep $cfg.defaults) -Override $sc
    $name = $merged.name
    Write-Host "`n=== Scenario: $name ===" -ForegroundColor Green
    $scDir = Join-Path $sessionDir $name
    $logsDir = Join-Path $scDir 'logs'
    New-Item -ItemType Directory -Force -Path $logsDir | Out-Null
    $merged | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $scDir 'scenario.json') -Encoding UTF8

    # Refresh agent config from the live configDir (so we always test the CURRENT contract).
    Copy-Item (Join-Path $configDir '*.json') $agentDir -Force
    Copy-Item (Join-Path $configDir 'nlog.config') $agentDir -Force -ErrorAction SilentlyContinue

    # Optional per-scenario logging-tier override (patch the agent's appsettings copy).
    if ($sc.ContainsKey('agentOverrides') -and $sc.agentOverrides.ContainsKey('loggingTier')) {
        $appPath = Join-Path $agentDir 'appsettings.json'
        $app = Get-Content $appPath -Raw | ConvertFrom-Json
        $app.Logging.LogLevel.'Philips.IBE' = $sc.agentOverrides.loggingTier
        $app | ConvertTo-Json -Depth 10 | Set-Content $appPath -Encoding UTF8
    }

    $env:IBE_LOG_DIR = $logsDir
    if ($serverGcMode -eq 'runtime') { $env:DOTNET_gcServer = if ($serverGc) { '1' } else { '0' } }

    $stopFlag = Join-Path $scDir 'stop.flag'
    $readyFlag = Join-Path $scDir 'ready.flag'
    Remove-Item $stopFlag, $readyFlag -ErrorAction SilentlyContinue

    # 1) sink
    $sink = Start-Process -FilePath $toolExe -PassThru -NoNewWindow -RedirectStandardOutput (Join-Path $logsDir 'sink.out') -RedirectStandardError (Join-Path $logsDir 'sink.err') `
        -ArgumentList @('sink', '--contract', $contractPath, '--scenario', (Join-Path $scDir 'scenario.json'), '--out', $scDir, '--stop', $stopFlag, '--ready', $readyFlag)
    if (-not (Wait-File $readyFlag 15)) { Write-Warning 'sink did not signal ready'; }

    # 2) agent
    $agent = Start-Process -FilePath $agentExe -WorkingDirectory $agentDir -PassThru -NoNewWindow `
        -RedirectStandardOutput (Join-Path $logsDir 'agent.out') -RedirectStandardError (Join-Path $logsDir 'agent.err')
    if (-not (Wait-Port $inboundPort $cfg.agent.startupTimeoutSec)) {
        Write-Warning "agent inbound port $inboundPort did not open"
    }

    # 3) counters (optional)
    $dur = [int]$merged.warmupSec + [int]$merged.durationSec
    $counters = $null
    if ($cfg.report.captureCounters) {
        $counters = Start-Counters -AgentPid $agent.Id -OutFile (Join-Path $scDir 'counters.csv') -DurationSec $dur
    }

    # 4) load (blocks for warmup + duration)
    & $toolExe load --contract $contractPath --scenario (Join-Path $scDir 'scenario.json') --out $scDir

    # 5) teardown: stop sink, agent, counters
    Set-Content $stopFlag 'stop'
    if (-not $sink.HasExited) { $sink.WaitForExit(10000) | Out-Null }
    Stop-Process -Id $agent.Id -Force -ErrorAction SilentlyContinue
    if ($null -ne $counters -and -not $counters.HasExited) { $counters.WaitForExit(8000) | Out-Null; if (-not $counters.HasExited) { Stop-Process -Id $counters.Id -Force -ErrorAction SilentlyContinue } }
    Remove-Item Env:\IBE_LOG_DIR -ErrorAction SilentlyContinue
}

Stop-AgentProcesses

# ---- Report ------------------------------------------------------------------------------------
$reportArgs = @('report', '--session', $sessionDir, '--out', (Join-Path $sessionDir 'session.html'))
$baselineDir = if ($Baseline) { $Baseline } elseif ($cfg.report.baselineDir) { $cfg.report.baselineDir } else { '' }
if ($baselineDir) { $reportArgs += @('--baseline', $baselineDir) }
& $toolExe @reportArgs

$reportPath = Join-Path $sessionDir 'session.html'
Write-Host "`nReport: $reportPath" -ForegroundColor Cyan
if ($cfg.report.openAfter -and -not $isBundle) { Invoke-Item $reportPath }
