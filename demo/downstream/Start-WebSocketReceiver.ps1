# -----------------------------------------------------------------------------
# Start-WebSocketReceiver.ps1 - external downstream system (WebSocket).
#
# This is the system the IBE Agent DELIVERS to over a WebSocket outbound comm
# point. The agent dials this receiver (ws://), then for each binary message it
# delivers, this script prints it and (when reply is enabled) writes an ack back
# on the same connection - exactly what the agent's WebSocket outbound endpoint
# reads when ExpectReply is true (Enhanced-ack / Response modes relay that reply
# back to the original source).
#
# Run this in its own terminal and leave it running for the demonstration.
# Stop it with Ctrl+C. To exercise the retry / store-and-forward path, stop this
# receiver, send messages (delivery fails and is retried), then start it again.
# -----------------------------------------------------------------------------
[CmdletBinding()]
param(
    [string]$Prefix = 'http://localhost:5203/ibe/ws/',           # HttpListener prefix; the agent connects ws://localhost:5203/ibe/ws/
    [string]$AckPayload = 'MSA|AA|RECEIVED-BY-WS-DOWNSTREAM',
    [switch]$NoReply,                                            # do not send an ack back (simulate a fire-and-forget sink)
    [switch]$Nack                                               # reply with a negative ack (MSA|AE) to exercise failure handling
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

$component = 'ws-downstream'
if ($Nack) { $AckPayload = 'MSA|AE|REJECTED-BY-WS-DOWNSTREAM' }

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add($Prefix)
$listener.Start()

Write-DemoLog -Component $component -Level INFO -Message "Downstream WebSocket system listening on $Prefix (reply: $(-not $NoReply))."
Write-DemoLog -Component $component -Level INFO -Message "Waiting for the IBE Agent to deliver messages. Press Ctrl+C to stop."

# Handles one accepted WebSocket connection, looping over every message the agent delivers on it.
function Invoke-WebSocketConnection {
    param([Parameter(Mandatory)]$Context)

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
            Write-DemoLog -Component $component -Level RECV -Message "Message delivered ($($bytes.Length) bytes):"
            Write-Hl7 -Message (Get-Utf8String -Bytes $bytes)

            if (-not $NoReply) {
                $replyBytes = Get-Utf8Bytes -Text $AckPayload
                $replySegment = New-Object System.ArraySegment[byte] (, $replyBytes)
                [void]$socket.SendAsync($replySegment, [System.Net.WebSockets.WebSocketMessageType]::Binary, $true, [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
                Write-DemoLog -Component $component -Level ACK -Message "Returned acknowledgement: $AckPayload"
            }
        }
    }
    catch {
        Write-DemoLog -Component $component -Level WARN -Message "Connection ended: $($_.Exception.Message)"
    }
    finally {
        $socket.Dispose()
    }
}

try {
    while ($true) {
        $context = $listener.GetContext()   # blocks until the agent connects
        if (-not $context.Request.IsWebSocketRequest) {
            $context.Response.StatusCode = 400
            $context.Response.Close()
            continue
        }
        Write-DemoLog -Component $component -Level INFO -Message "IBE Agent connected from $($context.Request.RemoteEndPoint)."
        Invoke-WebSocketConnection -Context $context
    }
}
finally {
    $listener.Stop()
    $listener.Close()
    Write-DemoLog -Component $component -Level INFO -Message "Downstream WebSocket system stopped."
}
