# -----------------------------------------------------------------------------
# Common.ps1 - shared helpers for the IBE Agent demonstration kit.
#
# Dot-sourced by the upstream sender and downstream receiver scripts. Defines
# only functions and constants (no side effects), so it is safe to load from
# any of the demo scripts.
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

# --- Logging: timestamped, level-tagged, human-readable. No decoration. --------
function Write-DemoLog {
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet('INFO', 'WARN', 'ERROR', 'SENT', 'RECV', 'ACK')][string]$Level = 'INFO',
        [string]$Component = 'demo'
    )
    $timestamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss.fff')
    $line = '{0}  {1,-4}  [{2}]  {3}' -f $timestamp, $Level, $Component, $Message
    $color = switch ($Level) {
        'ERROR' { 'Red' }
        'WARN'  { 'Yellow' }
        'SENT'  { 'Cyan' }
        'RECV'  { 'Green' }
        'ACK'   { 'Green' }
        default { 'Gray' }
    }
    Write-Host $line -ForegroundColor $color
}

# Prints an HL7 message with one segment per line and a light indent, so it is
# easy to read in the terminal during a demonstration.
function Write-Hl7 {
    param(
        [Parameter(Mandatory)][string]$Message,
        [string]$Indent = '    '
    )
    foreach ($segment in ($Message -split "`r|`n")) {
        if ($segment.Trim()) { Write-Host ($Indent + $segment) -ForegroundColor DarkGray }
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

# Reads one MLLP frame from a stream. Returns a result with an explicit status
# so callers can distinguish a real message from an idle timeout or a closed
# connection.
#   Status = 'Frame'   -> Payload holds the de-framed bytes
#   Status = 'Timeout' -> no data within ReadTimeoutMs
#   Status = 'Closed'  -> peer closed the connection
function Read-MllpFrame {
    param(
        [Parameter(Mandatory)][System.IO.Stream]$Stream,
        [int]$ReadTimeoutMs = 10000
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

# --- HL7 message helpers -----------------------------------------------------
# Loads an HL7 message from a file (one segment per line), normalises segment
# separators to carriage returns, and stamps a fresh message control id into
# MSH-10 so every send is uniquely identifiable end to end.
function New-Hl7MessageFromFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$ControlId
    )
    if (-not $ControlId) {
        $ControlId = 'MSG{0}' -f (Get-Date).ToString('yyyyMMddHHmmssfff')
    }

    $segments = Get-Content -LiteralPath $Path | Where-Object { $_.Trim() }
    $result = foreach ($segment in $segments) {
        if ($segment.StartsWith('MSH')) {
            $fields = $segment -split '\|'
            while ($fields.Count -lt 10) { $fields += '' }
            $fields[9] = $ControlId          # MSH-10, message control id
            ($fields -join '|')
        }
        else {
            $segment
        }
    }

    return [pscustomobject]@{
        ControlId = $ControlId
        Text      = ($result -join "`r")
    }
}
