# -----------------------------------------------------------------------------
# Send-WebSocketMessage.ps1 - upstream WebSocket comm point.
#
# Stands in for a real upstream system that feeds the IBE Agent over a
# WebSocket inbound endpoint. It connects, sends one binary message, and (when
# an acknowledgement is expected) reads back one reply frame on the same
# connection, the way WebSocketAckToken writes the ack for WebSocketInbound.
#
# Emits a single result object describing what happened.
# -----------------------------------------------------------------------------
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Uri,             # e.g. ws://localhost:19001/ibe/ws/
    [Parameter(Mandatory)][string]$Payload,
    [bool]$ExpectAck = $true,
    [int]$AckTimeoutMs = 15000,
    [int]$ConnectTimeoutMs = 5000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

$result = [ordered]@{
    Transport   = 'websocket'
    Sent        = $false
    AckReceived = $false
    AckText     = $null
    StatusCode  = $null
    LatencyMs   = $null
    Error       = $null
}

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$socket = New-Object System.Net.WebSockets.ClientWebSocket

try {
    $connectCts = New-Object System.Threading.CancellationTokenSource($ConnectTimeoutMs)
    [void]$socket.ConnectAsync([Uri]$Uri, $connectCts.Token).GetAwaiter().GetResult()

    $payloadBytes = Get-Utf8Bytes -Text $Payload
    $sendSegment = New-Object System.ArraySegment[byte] (, $payloadBytes)
    [void]$socket.SendAsync($sendSegment, [System.Net.WebSockets.WebSocketMessageType]::Binary, $true, [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
    $result.Sent = $true

    if ($ExpectAck) {
        $ackCts = New-Object System.Threading.CancellationTokenSource($AckTimeoutMs)
        $buffer = New-Object byte[] 65536
        $acc = New-Object System.IO.MemoryStream
        try {
            $completed = $false
            do {
                $segment = New-Object System.ArraySegment[byte] (, $buffer)
                $recv = $socket.ReceiveAsync($segment, $ackCts.Token).GetAwaiter().GetResult()
                if ($recv.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) { break }
                $acc.Write($buffer, 0, $recv.Count)
                $completed = $recv.EndOfMessage
            } while (-not $completed)

            if ($acc.Length -gt 0) {
                $result.AckReceived = $true
                $result.AckText = Get-Utf8String -Bytes $acc.ToArray()
            }
        }
        catch [System.OperationCanceledException] {
            # timed out waiting for the ack; leave AckReceived = $false
        }
    }
}
catch {
    $result.Error = $_.Exception.Message
    $inner = $_.Exception.InnerException
    if ($inner -and $inner.Message) { $result.Error = $inner.Message }
}
finally {
    try {
        if ($socket.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
            [void]$socket.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, $null, [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
        }
    }
    catch { }
    $socket.Dispose()
    $stopwatch.Stop()
    $result.LatencyMs = [int]$stopwatch.ElapsedMilliseconds
}

[pscustomobject]$result
