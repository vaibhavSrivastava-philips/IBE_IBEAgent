# -----------------------------------------------------------------------------
# Send-HttpMessage.ps1 - upstream HTTP comm point.
#
# Stands in for a real upstream system that feeds the IBE Agent over an HTTP
# inbound endpoint. It POSTs one message as raw bytes and captures the status
# code and response body. It does not throw on non-success status codes, because
# those outcomes (for example 504 when a no-ack contract holds and releases the
# request) are legitimate results the workflow needs to assert on.
#
# Emits a single result object describing what happened.
# -----------------------------------------------------------------------------
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Uri,
    [Parameter(Mandatory)][string]$Payload,
    [int]$TimeoutSec = 40
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

$result = [ordered]@{
    Transport   = 'http'
    Sent        = $false
    AckReceived = $false
    AckText     = $null
    StatusCode  = $null
    LatencyMs   = $null
    Error       = $null
}

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds($TimeoutSec)

try {
    $content = [System.Net.Http.ByteArrayContent]::new((Get-Utf8Bytes -Text $Payload))
    $content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new('application/octet-stream')

    $response = $client.PostAsync($Uri, $content).GetAwaiter().GetResult()
    $body = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()

    $result.Sent = $true
    $result.StatusCode = [int]$response.StatusCode
    $result.AckText = if ($body.Length -gt 0) { Get-Utf8String -Bytes $body } else { '' }
    # A 2xx response body is the agent's reply (normal/enhanced ack or response payload).
    $result.AckReceived = ($result.StatusCode -ge 200 -and $result.StatusCode -lt 300 -and $body.Length -gt 0)
}
catch {
    $result.Sent = $true
    $result.Error = $_.Exception.Message
    $inner = $_.Exception.InnerException
    if ($inner -and $inner.Message) { $result.Error = $inner.Message }
}
finally {
    $client.Dispose()
    $stopwatch.Stop()
    $result.LatencyMs = [int]$stopwatch.ElapsedMilliseconds
}

[pscustomobject]$result
