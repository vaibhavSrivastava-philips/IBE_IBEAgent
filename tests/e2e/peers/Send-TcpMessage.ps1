# -----------------------------------------------------------------------------
# Send-TcpMessage.ps1 - upstream TCP/MLLP comm point.
#
# Stands in for a real upstream system that feeds the IBE Agent over a TCP
# inbound endpoint. It connects, sends one MLLP-framed message, and (when an
# acknowledgement is expected) reads back the MLLP-framed reply the agent writes
# on the same connection.
#
# Emits a single result object describing what happened.
# -----------------------------------------------------------------------------
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$Port,
    [Parameter(Mandatory)][string]$Payload,
    [string]$HostName = '127.0.0.1',
    [bool]$ExpectAck = $true,
    [int]$AckTimeoutMs = 15000,
    [int]$ConnectTimeoutMs = 5000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

$result = [ordered]@{
    Transport     = 'tcp'
    Sent          = $false
    AckReceived   = $false
    AckText       = $null
    StatusCode    = $null
    LatencyMs     = $null
    Error         = $null
}

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$client = New-Object System.Net.Sockets.TcpClient

try {
    $async = $client.BeginConnect($HostName, $Port, $null, $null)
    if (-not ($async.AsyncWaitHandle.WaitOne($ConnectTimeoutMs) -and $client.Connected)) {
        throw "Connection to ${HostName}:${Port} timed out."
    }
    $client.EndConnect($async)

    $stream = $client.GetStream()
    $frame = New-MllpFrame -Payload (Get-Utf8Bytes -Text $Payload)
    $stream.Write($frame, 0, $frame.Length)
    $stream.Flush()
    $result.Sent = $true

    if ($ExpectAck) {
        $reply = Read-MllpFrame -Stream $stream -ReadTimeoutMs $AckTimeoutMs
        if ($reply.Status -eq 'Frame') {
            $result.AckReceived = $true
            $result.AckText = Get-Utf8String -Bytes $reply.Payload
        }
    }
}
catch {
    $result.Error = $_.Exception.Message
}
finally {
    $client.Close()
    $stopwatch.Stop()
    $result.LatencyMs = [int]$stopwatch.ElapsedMilliseconds
}

[pscustomobject]$result
