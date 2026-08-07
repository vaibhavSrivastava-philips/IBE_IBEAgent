<#
.SYNOPSIS
  Packages the IBE Agent + the IbePerf harness into a self-contained, copy-pasteable folder (and zip)
  that runs on a high-performance test server with NO .NET SDK or source checkout required.

.DESCRIPTION
  Publishes both the agent and the IbePerf tool self-contained for the target runtime (default win-x64,
  Server-GC baked in), copies the harness scripts + live config, drops a Run-Perf.cmd entry point, and
  zips the result. On the target: unzip, run Run-Perf.cmd (or pwsh Invoke-PerfSuite.ps1) -> HTML report.

.EXAMPLE
  pwsh -File perf/Export-PerfBundle.ps1
  pwsh -File perf/Export-PerfBundle.ps1 -Runtime linux-x64 -Output C:\out\ibe-perf
#>
[CmdletBinding()]
param(
    [string]$Config = "$PSScriptRoot/perf.config.json",
    [string]$Runtime = '',
    [string]$Output = '',
    [switch]$NoZip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/Common.ps1')

$cfg = ConvertTo-HashtableDeep (Get-Content $Config -Raw | ConvertFrom-Json)
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Runtime) { $Runtime = [string]$cfg.build.runtime }
$serverGc = [bool]$cfg.build.serverGc
$selfContained = [bool]$cfg.build.selfContained
if (-not $Output) { $Output = Join-Path $PSScriptRoot "bundle/ibe-perf-$Runtime" }

Write-Host "Exporting portable perf bundle" -ForegroundColor Cyan
Write-Host "  runtime      : $Runtime"
Write-Host "  self-contained: $selfContained  serverGc: $serverGc"
Write-Host "  output       : $Output"

if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Output | Out-Null
$agentOut = Join-Path $Output 'agent'
$toolOut = Join-Path $Output 'tools/IbePerf'

$scArg = "-p:PublishSingleFile=false"
$gc = @("-p:ServerGarbageCollection=$($serverGc.ToString().ToLower())", "-p:ConcurrentGarbageCollection=$($cfg.build.concurrentGc.ToString().ToLower())")

Write-Host "`nPublishing agent (self-contained=$selfContained)..." -ForegroundColor Cyan
& dotnet publish (Join-Path $repoRoot $cfg.agent.projectPath) -c $cfg.build.configuration -r $Runtime `
    --self-contained $($selfContained.ToString().ToLower()) -o $agentOut --nologo @gc
if ($LASTEXITCODE -ne 0) { throw 'agent publish failed' }

Write-Host "Publishing IbePerf tool..." -ForegroundColor Cyan
& dotnet publish (Join-Path $repoRoot 'perf/tools/IbePerf/IbePerf.csproj') -c Release -r $Runtime `
    --self-contained $($selfContained.ToString().ToLower()) -o $toolOut --nologo
if ($LASTEXITCODE -ne 0) { throw 'IbePerf publish failed' }

# Harness scripts + config + docs.
Copy-Item (Join-Path $PSScriptRoot 'Invoke-PerfSuite.ps1') $Output -Force
Copy-Item (Join-Path $PSScriptRoot 'perf.config.json') $Output -Force
Copy-Item (Join-Path $PSScriptRoot 'perf.config.schema.json') $Output -Force
Copy-Item (Join-Path $PSScriptRoot 'README.md') $Output -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $PSScriptRoot 'lib') $Output -Recurse -Force
New-Item -ItemType Directory -Force -Path (Join-Path $Output 'config') | Out-Null
Copy-Item (Join-Path $repoRoot ($cfg.agent.configDir + '/*')) (Join-Path $Output 'config') -Force

# Entry point for the target server.
@"
@echo off
where pwsh >nul 2>nul && (pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0Invoke-PerfSuite.ps1" %*) || (powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Invoke-PerfSuite.ps1" %*)
"@ | Set-Content (Join-Path $Output 'Run-Perf.cmd') -Encoding ascii

@"
IBE Agent portable performance bundle
=====================================
1. Copy this whole folder to the test server.
2. (Optional) Edit perf.config.json to change scenarios / Server-GC / durations.
3. (Optional) Adjust config\contractData.json to the topology you want to test.
4. Double-click Run-Perf.cmd  (or:  pwsh -File Invoke-PerfSuite.ps1)
5. Open results\<timestamp>\session.html

Notes:
- The agent and IbePerf are self-contained: no .NET install needed on the target.
- GC/CPU capture is optional; install 'dotnet-counters' on the target to enable it.
"@ | Set-Content (Join-Path $Output 'HOW-TO-RUN.txt') -Encoding ascii

Write-Host "`nBundle ready: $Output" -ForegroundColor Green
if (-not $NoZip) {
    $zip = "$Output.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $Output '*') -DestinationPath $zip
    Write-Host "Zipped: $zip" -ForegroundColor Green
}
