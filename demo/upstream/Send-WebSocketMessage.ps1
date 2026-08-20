# -----------------------------------------------------------------------------
# Send-WebSocketMessage.ps1 - external upstream system (WebSocket).
#
# This is the system that FEEDS the IBE Agent over a WebSocket inbound comm
# point. It opens one persistent WebSocket connection to the agent's WS input
# and, each time you press Enter, sends one HL7 message as a single binary
# WebSocket message and shows the acknowledgement the agent returns (if the
# contract is configured to send one). Unlike MLLP there is no framing - each
# WebSocket message IS one HL7 message.
#
# Run this in its own terminal. Press Enter to send a message; type Q then Enter
# to quit. The connection auto-reconnects if the agent restarts.
# -----------------------------------------------------------------------------
[CmdletBinding()]
param(
    [string]$Uri = 'ws://localhost:5103/ibe/ws/',
    [string]$MessageFile = (Join-Path $PSScriptRoot '..\messages\adt-a01.hl7'),
    [switch]$NoAck,                                  # set when the contract has no Acknowledgement/Response (skip the ack wait)
    [int]$AckTimeoutSeconds = 15
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

$component = 'ws-upstream'

function Connect-WebSocket {
    param([Parameter(Mandatory)][string]$Uri)
    $socket = [System.Net.WebSockets.ClientWebSocket]::new()
    $cts = [System.Threading.CancellationTokenSource]::new(5000)
    $socket.ConnectAsync([Uri]$Uri, $cts.Token).GetAwaiter().GetResult()
    return $socket
}

# Reads one complete WebSocket message (accumulating fragments); $null on close or timeout.
function Receive-WebSocketMessage {
    param([Parameter(Mandatory)]$Socket, [int]$TimeoutMs)
    $cts = [System.Threading.CancellationTokenSource]::new($TimeoutMs)
    $buffer = New-Object byte[] 65536
    $acc = New-Object System.IO.MemoryStream
    try {
        do {
            $segment = New-Object System.ArraySegment[byte] (, $buffer)
            $result = $Socket.ReceiveAsync($segment, $cts.Token).GetAwaiter().GetResult()
            if ($result.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) { return $null }
            $acc.Write($buffer, 0, $result.Count)
        } while (-not $result.EndOfMessage)
        return $acc.ToArray()
    }
    catch [System.OperationCanceledException] { return $null }
}

Write-DemoLog -Component $component -Level INFO -Message "Connecting to the IBE Agent WebSocket input at $Uri ..."
$socket = Connect-WebSocket -Uri $Uri
Write-DemoLog -Component $component -Level INFO -Message "Connected. Press Enter to send an HL7 message, or type Q then Enter to quit."

$sequence = 0
try {
    while ($true) {
        $entry = Read-Host 'Send'
        if ($entry -match '^\s*[qQ]') { break }

        if ($socket.State -ne [System.Net.WebSockets.WebSocketState]::Open) {
            Write-DemoLog -Component $component -Level WARN -Message "Socket is $($socket.State); reconnecting ..."
            try { $socket.Dispose() } catch { }
            $socket = Connect-WebSocket -Uri $Uri
        }

        $sequence++
        $controlId = 'DEMO{0:D4}-{1}' -f $sequence, (Get-Date).ToString('HHmmss')
        $message = New-Hl7MessageFromFile -Path $MessageFile -ControlId $controlId
        $bytes = Get-Utf8Bytes -Text $message.Text

        $segment = New-Object System.ArraySegment[byte] (, $bytes)
        $socket.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Binary, $true, [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
        Write-DemoLog -Component $component -Level SENT -Message "Sent message (control id $($message.ControlId), $($bytes.Length) bytes):"
        Write-Hl7 -Message $message.Text

        if (-not $NoAck) {
            $reply = Receive-WebSocketMessage -Socket $socket -TimeoutMs ($AckTimeoutSeconds * 1000)
            if ($null -ne $reply) {
                Write-DemoLog -Component $component -Level ACK -Message "Acknowledgement received from the agent:"
                Write-Hl7 -Message (Get-Utf8String -Bytes $reply)
            }
            else {
                Write-DemoLog -Component $component -Level INFO -Message "No acknowledgement within ${AckTimeoutSeconds}s (expected for a no-ack contract)."
            }
        }
    }
}
finally {
    try {
        if ($socket.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
            $socket.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, $null, [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
        }
    }
    catch { }
    $socket.Dispose()
    Write-DemoLog -Component $component -Level INFO -Message "Disconnected from the IBE Agent."
}
