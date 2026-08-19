# -----------------------------------------------------------------------------
# Common.ps1 - shared helpers for the IBE Agent end-to-end test harness.
#
# Dot-sourced by the workflow orchestrator and by every peer script. Contains no
# top-level side effects; it only defines functions and constants so it can be
# loaded safely from independent processes.
# -----------------------------------------------------------------------------

Set-StrictMode -Version Latest

# --- MLLP (Minimal Lower Layer Protocol) framing bytes, per HL7 transport. -----
$script:MllpStartBlock = [byte]0x0B   # <VT>
$script:MllpEndBlock1  = [byte]0x1C   # <FS>
$script:MllpEndBlock2  = [byte]0x0D   # <CR>

function Get-Utf8Bytes {
    param([Parameter(Mandatory)][string]$Text)
    return [System.Text.Encoding]::UTF8.GetBytes($Text)
}

function Get-Utf8String {
    param([Parameter(Mandatory)][byte[]]$Bytes)
    return [System.Text.Encoding]::UTF8.GetString($Bytes)
}

# --- Logging -----------------------------------------------------------------
# Timestamped, level-tagged, component-tagged lines. Human-readable, no symbols
# or decoration beyond the fields. Optionally mirrored to a log file.
function Write-HarnessLog {
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet('INFO', 'WARN', 'ERROR', 'STEP', 'PASS', 'FAIL')][string]$Level = 'INFO',
        [string]$Component = 'harness',
        [string]$LogFile,
        [switch]$NoConsole
    )

    $timestamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss.fff')
    $line = '{0}  {1,-5}  [{2}]  {3}' -f $timestamp, $Level, $Component, $Message

    if (-not $NoConsole) {
        $color = switch ($Level) {
            'ERROR' { 'Red' }
            'FAIL'  { 'Red' }
            'WARN'  { 'Yellow' }
            'PASS'  { 'Green' }
            'STEP'  { 'Cyan' }
            default { 'Gray' }
        }
        Write-Host $line -ForegroundColor $color
    }

    if ($LogFile) {
        for ($attempt = 0; $attempt -lt 5; $attempt++) {
            try {
                $stream = [System.IO.File]::Open($LogFile, 'Append', 'Write', 'ReadWrite')
                try {
                    $writer = New-Object System.IO.StreamWriter($stream)
                    $writer.WriteLine($line)
                    $writer.Flush()
                    $writer.Dispose()
                }
                finally { $stream.Dispose() }
                break
            }
            catch [System.IO.IOException] {
                Start-Sleep -Milliseconds 25
            }
        }
    }
}

# --- MLLP frame construction and parsing -------------------------------------
function New-MllpFrame {
    param([Parameter(Mandatory)][byte[]]$Payload)
    $frame = New-Object byte[] ($Payload.Length + 3)
    $frame[0] = $script:MllpStartBlock
    [System.Array]::Copy($Payload, 0, $frame, 1, $Payload.Length)
    $frame[$frame.Length - 2] = $script:MllpEndBlock1
    $frame[$frame.Length - 1] = $script:MllpEndBlock2
    return , $frame
}

# Reads a single MLLP frame from a stream. Returns a result with an explicit
# status so callers can tell a real message apart from an idle timeout or a
# closed connection.
#   Status = 'Frame'   -> Payload holds the de-framed bytes
#   Status = 'Timeout' -> no data within ReadTimeoutMs
#   Status = 'Closed'  -> peer closed the connection
function Read-MllpFrame {
    param(
        [Parameter(Mandatory)][System.IO.Stream]$Stream,
        [int]$ReadTimeoutMs = 5000
    )

    try { $Stream.ReadTimeout = $ReadTimeoutMs } catch { }

    $buffer = New-Object System.Collections.Generic.List[byte]
    $inMessage = $false
    $sawFs = $false
    $one = New-Object byte[] 1

    while ($true) {
        try {
            $n = $Stream.Read($one, 0, 1)
        }
        catch [System.IO.IOException] {
            return [pscustomobject]@{ Status = 'Timeout'; Payload = $null }
        }

        if ($n -le 0) {
            return [pscustomobject]@{ Status = 'Closed'; Payload = $null }
        }

        $b = $one[0]

        if (-not $inMessage) {
            if ($b -eq $script:MllpStartBlock) { $inMessage = $true; $sawFs = $false; $buffer.Clear() }
            continue
        }
        if ($sawFs) {
            if ($b -eq $script:MllpEndBlock2) {
                return [pscustomobject]@{ Status = 'Frame'; Payload = $buffer.ToArray() }
            }
            $buffer.Add($script:MllpEndBlock1)
            $buffer.Add($b)
            $sawFs = $false
            continue
        }
        if ($b -eq $script:MllpEndBlock1) { $sawFs = $true; continue }
        $buffer.Add($b)
    }
}

# --- Capture files (newline-delimited JSON, one record per received message) ---
function Add-CaptureRecord {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][hashtable]$Record
    )
    $json = ($Record | ConvertTo-Json -Compress -Depth 6)
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        try {
            $stream = [System.IO.File]::Open($Path, 'Append', 'Write', 'ReadWrite')
            try {
                $writer = New-Object System.IO.StreamWriter($stream)
                $writer.WriteLine($json)
                $writer.Flush()
                $writer.Dispose()
            }
            finally { $stream.Dispose() }
            return
        }
        catch [System.IO.IOException] {
            Start-Sleep -Milliseconds 25
        }
    }
}

function Get-CaptureText {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
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
        catch [System.IO.IOException] {
            Start-Sleep -Milliseconds 25
        }
    }
    return ''
}

# Polls a capture file until a line containing the marker appears, or timeout.
function Wait-ForCapture {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Marker,
        [int]$TimeoutMs = 5000
    )
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    while ((Get-Date) -lt $deadline) {
        $text = Get-CaptureText -Path $Path
        if ($text -and $text.Contains($Marker)) { return $true }
        Start-Sleep -Milliseconds 100
    }
    return $false
}

# Returns the Content-Type recorded for the first capture line whose body contains the marker
# (used to assert media-type classification / content-type relay on an HTTP outbound leg).
function Get-CaptureContentType {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Marker
    )
    $text = Get-CaptureText -Path $Path
    if (-not $text) { return $null }
    foreach ($line in ($text -split "`r?`n")) {
        if (-not $line.Trim()) { continue }
        try { $rec = $line | ConvertFrom-Json } catch { continue }
        if (($rec.PSObject.Properties.Name -contains 'text') -and "$($rec.text)".Contains($Marker)) {
            if ($rec.PSObject.Properties.Name -contains 'contentType') { return $rec.contentType }
            return $null
        }
    }
    return $null
}

# --- Port readiness ----------------------------------------------------------
# A bare TCP connect that confirms a listener is accepting on the port. Used for
# both TCP and HTTP inbound endpoints (HttpListener also binds a TCP port). It
# deliberately does not send any bytes, so it never injects a message into the
# agent pipeline.
function Wait-PortOpen {
    param(
        [Parameter(Mandatory)][int]$Port,
        [string]$HostName = '127.0.0.1',
        [int]$TimeoutMs = 15000
    )
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    while ((Get-Date) -lt $deadline) {
        $client = New-Object System.Net.Sockets.TcpClient
        try {
            $async = $client.BeginConnect($HostName, $Port, $null, $null)
            if ($async.AsyncWaitHandle.WaitOne(500) -and $client.Connected) {
                $client.EndConnect($async)
                return $true
            }
        }
        catch { }
        finally { $client.Close() }
        Start-Sleep -Milliseconds 150
    }
    return $false
}
