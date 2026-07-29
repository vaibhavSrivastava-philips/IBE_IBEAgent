# -----------------------------------------------------------------------------
# Start-Agent.ps1 - runs the IBE Agent against the repository's /config files.
#
# Builds and starts the main agent host in the console. The agent reads its
# topology from config/contractData.json and config/catalogData.json, starts the
# inbound listeners and outbound connectors declared there, and logs to this
# window. Stop it with Ctrl+C.
#
# The agent reads its configuration once at startup, so after editing
# config/contractData.json, stop the agent (Ctrl+C) and run this script again.
# -----------------------------------------------------------------------------
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$serviceProject = Join-Path $repoRoot 'src\Philips.IBE.IBEAgent\hosts\Philips.IBE.IBEAgent.Service\Philips.IBE.IBEAgent.Service.csproj'

Write-Host ("Starting the IBE Agent ({0}) against config/ ..." -f $Configuration) -ForegroundColor Cyan
Write-Host 'The agent will print its startup summary, then run until you press Ctrl+C.' -ForegroundColor Gray
Write-Host ''

& dotnet run --project $serviceProject -c $Configuration
