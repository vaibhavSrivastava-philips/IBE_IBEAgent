# Common.ps1 - shared helpers for the IBE Agent performance harness.
Set-StrictMode -Version Latest

function ConvertTo-HashtableDeep {
    param($InputObject)
    if ($null -eq $InputObject) { return $null }
    if ($InputObject -is [System.Collections.IDictionary]) {
        $h = @{}
        foreach ($k in $InputObject.Keys) { $h[$k] = ConvertTo-HashtableDeep $InputObject[$k] }
        return $h
    }
    if ($InputObject -is [pscustomobject]) {
        $h = @{}
        foreach ($p in $InputObject.PSObject.Properties) { $h[$p.Name] = ConvertTo-HashtableDeep $p.Value }
        return $h
    }
    if ($InputObject -is [object[]]) {
        return @($InputObject | ForEach-Object { ConvertTo-HashtableDeep $_ })
    }
    return $InputObject
}

# Deep-merges $Override onto a copy of $Base (both hashtables). Arrays are replaced wholesale.
function Merge-Config {
    param([hashtable]$Base, [hashtable]$Override)
    $result = @{}
    foreach ($k in $Base.Keys) { $result[$k] = $Base[$k] }
    if ($null -ne $Override) {
        foreach ($k in $Override.Keys) {
            if ($result.ContainsKey($k) -and ($result[$k] -is [hashtable]) -and ($Override[$k] -is [hashtable])) {
                $result[$k] = Merge-Config -Base $result[$k] -Override $Override[$k]
            } else {
                $result[$k] = $Override[$k]
            }
        }
    }
    return $result
}

function Get-GitInfo {
    $branch = $null; $commit = $null
    try { $branch = (& git rev-parse --abbrev-ref HEAD 2>$null) } catch { }
    try { $commit = (& git rev-parse --short HEAD 2>$null) } catch { }
    return @{ branch = $branch; commit = $commit }
}

function Get-SysInfo {
    param([bool]$ServerGc, [string]$ContractName)
    $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
    $os = Get-CimInstance Win32_OperatingSystem
    $git = Get-GitInfo
    $dotnet = try { (& dotnet --version) } catch { 'unknown' }
    return [ordered]@{
        machine   = $env:COMPUTERNAME
        cpu       = ($cpu.Name).Trim()
        cores     = $cpu.NumberOfLogicalProcessors
        ramGb     = [math]::Round($os.TotalVisibleMemorySize / 1MB, 1)
        os        = $os.Caption
        dotnet    = $dotnet
        gcMode    = if ($ServerGc) { 'Server' } else { 'Workstation' }
        serverGc  = $ServerGc
        gitBranch = $git.branch
        gitCommit = $git.commit
        contract  = $ContractName
        timestamp = (Get-Date).ToString('u')
    }
}

function Wait-Port {
    param([int]$Port, [int]$TimeoutSec)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
        try {
            $c = [System.Net.Sockets.TcpClient]::new()
            $c.Connect('127.0.0.1', $Port); $c.Close(); return $true
        } catch { Start-Sleep -Milliseconds 200 }
    }
    return $false
}

function Wait-File {
    param([string]$Path, [int]$TimeoutSec = 30)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
        if (Test-Path $Path) { return $true }
        Start-Sleep -Milliseconds 100
    }
    return $false
}

# Reads the first inbound listener port from contractData.json (for the agent health probe).
function Get-InboundPort {
    param([string]$ContractPath)
    $c = Get-Content $ContractPath -Raw | ConvertFrom-Json
    if ($c.Endpoints.PSObject.Properties.Name -contains 'TcpInbound' -and $c.Endpoints.TcpInbound.Count -gt 0) {
        return [int]$c.Endpoints.TcpInbound[0].Port
    }
    if ($c.Endpoints.PSObject.Properties.Name -contains 'HttpInbound' -and $c.Endpoints.HttpInbound.Count -gt 0) {
        $uri = [Uri]$c.Endpoints.HttpInbound[0].Prefix
        return [int]$uri.Port
    }
    throw "no inbound endpoint in $ContractPath"
}

function Stop-AgentProcesses {
    Get-Process -Name 'Philips.IBE.IBEAgent.Service', 'Philips.IBE.IBEAgent.ForwardService' -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

function Start-Counters {
    param([int]$AgentPid, [string]$OutFile, [int]$DurationSec)
    if (-not (Get-Command dotnet-counters -ErrorAction SilentlyContinue)) {
        Write-Host '  [counters] dotnet-counters not found; skipping GC/CPU capture.' -ForegroundColor DarkYellow
        return $null
    }
    $dur = [timespan]::FromSeconds($DurationSec + 3).ToString('hh\:mm\:ss')
    $args = @('collect', '--process-id', $AgentPid, '--format', 'csv', '--output', $OutFile,
        '--refresh-interval', '1', '--duration', $dur,
        '--counters', 'System.Runtime')
    return Start-Process -FilePath 'dotnet-counters' -ArgumentList $args -PassThru -WindowStyle Hidden
}
