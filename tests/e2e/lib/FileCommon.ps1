# -----------------------------------------------------------------------------
# FileCommon.ps1 - File comm-point helpers for the IBE Agent E2E harness.
#
# Dot-sourced alongside Common.ps1 by the File workflow orchestrator and the File
# peer script. A File source has no reply channel, so the observable source-side
# outcome is the file DISPOSITION (moved to processed/ or error/, left in place
# with an advanced watermark, or deleted) rather than a reply on a socket. These
# helpers drop files atomically and observe deliveries and dispositions on disk.
# No top-level side effects; only function definitions.
# -----------------------------------------------------------------------------

Set-StrictMode -Version Latest

# Read a file's text, tolerating a brief share/lock while another process writes.
function Read-FileTextSafe {
    param([Parameter(Mandatory)][string]$Path)
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        try {
            $stream = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
            try {
                $reader = New-Object System.IO.StreamReader($stream)
                $text = $reader.ReadToEnd()
                $reader.Dispose()
                return $text
            }
            finally { $stream.Dispose() }
        }
        catch [System.IO.IOException] { Start-Sleep -Milliseconds 25 }
        catch [System.UnauthorizedAccessException] { Start-Sleep -Milliseconds 25 }
    }
    return ''
}

# Wait until an agent log file contains a substring. A File inbound endpoint has
# no port to probe, so readiness is observed from its "polling {Directory}" line.
function Wait-ForAgentLog {
    param(
        [Parameter(Mandatory)][string]$LogFile,
        [Parameter(Mandatory)][string]$Pattern,
        [int]$TimeoutMs = 15000
    )
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $LogFile) {
            $text = Read-FileTextSafe -Path $LogFile
            if ($text -and $text.Contains($Pattern)) { return $true }
        }
        Start-Sleep -Milliseconds 150
    }
    return $false
}

# Atomically drop a file into a polled directory: write into a staging temp, then
# move it into place, so the poller never sees a partially written file.
function New-FileDrop {
    param(
        [Parameter(Mandatory)][string]$TargetDir,
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][string]$Content,
        [string]$StagingDir
    )
    if (-not $StagingDir) { $StagingDir = Join-Path $TargetDir '.staging' }
    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
    New-Item -ItemType Directory -Path $StagingDir -Force | Out-Null

    $staged = Join-Path $StagingDir ([System.IO.Path]::GetRandomFileName())
    # No BOM: real drops are BOM-free, and a leading BOM breaks JSON envelope parsing and base64 decoding.
    [System.IO.File]::WriteAllText($staged, $Content, (New-Object System.Text.UTF8Encoding($false)))

    $target = Join-Path $TargetDir $FileName
    Move-Item -LiteralPath $staged -Destination $target -Force
    return $target
}

# All top-level (non-.tmp, non-.staging) files under a directory whose content
# contains the marker. Used to confirm and count File-output deliveries.
function Get-OutputFilesWithMarker {
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][string]$Marker
    )
    $matches = @()
    if (-not (Test-Path -LiteralPath $Directory)) { return $matches }
    $files = Get-ChildItem -LiteralPath $Directory -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -ne '.tmp' }
    foreach ($f in $files) {
        $text = Read-FileTextSafe -Path $f.FullName
        if ($text -and $text.Contains($Marker)) {
            $matches += [pscustomobject]@{ Path = $f.FullName; Name = $f.Name; Text = $text }
        }
    }
    return , $matches
}

# Poll a directory for a top-level file whose content contains the marker.
function Wait-ForOutputFile {
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][string]$Marker,
        [int]$TimeoutMs = 15000
    )
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    while ((Get-Date) -lt $deadline) {
        $hits = Get-OutputFilesWithMarker -Directory $Directory -Marker $Marker
        if ($hits.Count -gt 0) {
            return [pscustomobject]@{ Found = $true; Path = $hits[0].Path; Name = $hits[0].Name; Text = $hits[0].Text }
        }
        Start-Sleep -Milliseconds 150
    }
    return [pscustomobject]@{ Found = $false; Path = $null; Name = $null; Text = $null }
}

# Poll <InputDir>/<Outcome> (recursively) for a file whose content contains the
# marker - the source-side disposition check for a File input (processed | error).
function Wait-ForDisposition {
    param(
        [Parameter(Mandatory)][string]$InputDir,
        [Parameter(Mandatory)][string]$Marker,
        [ValidateSet('processed', 'error')][string]$Outcome = 'processed',
        [int]$TimeoutMs = 15000
    )
    $folder = Join-Path $InputDir $Outcome
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $folder) {
            # Match by FILE NAME (which carries the message id): a disposed file keeps its name, and a
            # base64 payload has no plaintext marker in its content.
            $hit = Get-ChildItem -LiteralPath $folder -File -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.Name.Contains($Marker) } | Select-Object -First 1
            if ($hit) { return [pscustomobject]@{ Found = $true; Path = $hit.FullName; Name = $hit.Name } }
        }
        Start-Sleep -Milliseconds 150
    }
    return [pscustomobject]@{ Found = $false; Path = $null; Name = $null }
}

# Is a top-level file whose NAME carries the marker still present directly in the
# input dir? (Watermark mode leaves the file in place; Delete mode removes it.)
function Test-InputHasMarker {
    param(
        [Parameter(Mandatory)][string]$InputDir,
        [Parameter(Mandatory)][string]$Marker
    )
    if (-not (Test-Path -LiteralPath $InputDir)) { return $false }
    $hit = Get-ChildItem -LiteralPath $InputDir -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name.Contains($Marker) }
    return ($null -ne $hit)
}
