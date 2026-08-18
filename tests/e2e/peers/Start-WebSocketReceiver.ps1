# -----------------------------------------------------------------------------
# Start-WebSocketReceiver.ps1 - downstream WebSocket comm point.
#
# Stands in for a real downstream system that the IBE Agent delivers to over a
# WebSocket outbound leg. It accepts the WebSocket upgrade, then for each binary
# message received: records it to a capture file and (when enabled) writes back
# a reply on the same connection, exactly as WebSocketOutboundEndpoint expects
# when ExpectReply is true (Enhanced-ack / Response modes read that reply back
# on the same rented connection).
#
# Runs until the stop file appears or the process is terminated.
# -----------------------------------------------------------------------------
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Prefix,          # e.g. http://localhost:19091/ibe/ws/  (HttpListener upgrades ws:// on this prefix)
    [Parameter(Mandatory)][string]$CaptureFile,
    [Parameter(Mandatory)][string]$LogFile,
    [Parameter(Mandatory)][string]$StopFile,
    [string]$ReplyPayload = 'MSA|AA|WS-DOWNSTREAM-OK',
    [bool]$ReplyEnabled = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

$component = 'ws-receiver'
Write-HarnessLog -Level INFO -Component $component -LogFile $LogFile -NoConsole `
    -Message "Downstream WebSocket receiver starting on $Prefix (reply enabled: $ReplyEnabled)."

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add($Prefix)
$listener.Start()

function Invoke-WebSocketConnection {
    param($Context)

    $wsContext = $Context.AcceptWebSocketAsync([System.Management.Automation.Language.NullString]::Value).GetAwaiter().GetResult()
    $socket = $wsContext.WebSocket
    $buffer = New-Object byte[] 65536

    try {
        while ($socket.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
            $acc = New-Object System.IO.MemoryStream
            $result = $null
            do {
                $segment = New-Object System.ArraySegment[byte] (, $buffer)
                $result = $socket.ReceiveAsync($segment, [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
                if ($result.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
                    [void]$socket.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, $null, [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
                    return
                }
                $acc.Write($buffer, 0, $result.Count)
            } while (-not $result.EndOfMessage)

            $bytes = $acc.ToArray()
            $text = Get-Utf8String -Bytes $bytes

            Add-CaptureRecord -Path $CaptureFile -Record @{
                timestamp = (Get-Date).ToString('o')
                transport = 'websocket'
                length    = $bytes.Length
                text      = $text
            }
            $firstLine = ($text -split "`r")[0]
            Write-HarnessLog -Level INFO -Component $component -LogFile $LogFile -NoConsole `
                -Message "Received message ($($bytes.Length) bytes). MSH: $firstLine"

            if ($ReplyEnabled) {
                $replyBytes = Get-Utf8Bytes -Text $ReplyPayload
                $replySegment = New-Object System.ArraySegment[byte] (, $replyBytes)
                $socket.SendAsync($replySegment, [System.Net.WebSockets.WebSocketMessageType]::Binary, $true, [System.Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null
                Write-HarnessLog -Level INFO -Component $component -LogFile $LogFile -NoConsole `
                    -Message "Sent WebSocket acknowledgement: $ReplyPayload"
            }
        }
    }
    catch {
        Write-HarnessLog -Level WARN -Component $component -LogFile $LogFile -NoConsole `
            -Message "Connection handling ended: $($_.Exception.Message)"
    }
    finally {
        $socket.Dispose()
    }
}

try {
    while (-not (Test-Path -LiteralPath $StopFile)) {
        $contextTask = $listener.GetContextAsync()
        while (-not $contextTask.Wait(200)) {
            if (Test-Path -LiteralPath $StopFile) { break }
        }
        if (-not $contextTask.IsCompleted -or $contextTask.Status -ne [System.Threading.Tasks.TaskStatus]::RanToCompletion) { continue }

        $context = $contextTask.Result
        if (-not $context.Request.IsWebSocketRequest) {
            $context.Response.StatusCode = 400
            $context.Response.Close()
            continue
        }
        # Scenarios in this harness send one message per connection, so handling
        # it inline (like the TCP/HTTP receivers) keeps this dependency-free.
        Invoke-WebSocketConnection -Context $context
    }
}
finally {
    $listener.Stop()
    $listener.Close()
    Write-HarnessLog -Level INFO -Component $component -LogFile $LogFile -NoConsole `
        -Message "Downstream WebSocket receiver stopped."
}
