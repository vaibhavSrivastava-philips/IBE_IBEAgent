#Requires -Version 7.0
<#
.SYNOPSIS
    Builds and publishes the IBE Agent suite into ./publish, mirroring the field-install
    layout expected by Installation Script/ServiceInstaller.ps1.

    publish/IBEAgent        <- Philips.IBE.IBEAgent.Service.exe        (svc Philips.IBE.Agent)
    publish/ForwardService  <- Philips.IBE.IBEAgent.ForwardService.exe (svc Philips.IBE.Forward)
    publish/Web (+ wwwroot) <- Philips.IBE.Service.WebAgent.Server.exe (svc Philips.IBE.Web)
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$src = Join-Path $repoRoot 'src'
$publish = Join-Path $repoRoot 'publish'

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
New-Item -ItemType Directory -Path $publish | Out-Null

function Publish-DotnetHost([string]$Project, [string]$OutName) {
    $out = Join-Path $publish $OutName
    dotnet publish $Project -c $Configuration -r $Runtime --self-contained false -o $out
    if ($LASTEXITCODE -ne 0) { throw "publish failed: $Project" }
}

Publish-DotnetHost (Join-Path $src 'Philips.IBE.IBEAgent\hosts\Philips.IBE.IBEAgent.Service\Philips.IBE.IBEAgent.Service.csproj') 'IBEAgent'
Publish-DotnetHost (Join-Path $src 'Philips.IBE.IBEAgent\hosts\Philips.IBE.IBEAgent.ForwardService\Philips.IBE.IBEAgent.ForwardService.csproj') 'ForwardService'
Publish-DotnetHost (Join-Path $src 'Philips.IBE.Service.WebAgent\Philips.IBE.Service.WebAgent.Server\Philips.IBE.Service.WebAgent.Server.csproj') 'Web'

# Angular client -> publish/Web/wwwroot
$client = Join-Path $src 'Philips.IBE.Service.WebAgent\philips.ibe.service.webagent.client'
Push-Location $client
try {
    npm install --legacy-peer-deps
    if ($LASTEXITCODE -ne 0) { throw 'npm install failed' }
    npm run build -- --configuration production
    if ($LASTEXITCODE -ne 0) { throw 'npm build failed' }
}
finally { Pop-Location }

$dist = Join-Path $client 'dist\philips.ibe.service.webagent.client\browser'
$wwwroot = Join-Path $publish 'Web\wwwroot'
robocopy $dist $wwwroot /S | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'UI copy failed' }

# Shared domain config + install/HA scripts alongside the binaries
Copy-Item (Join-Path $repoRoot 'config\*') $publish -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'Installation Script\*') $publish -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Build complete -> $publish"
