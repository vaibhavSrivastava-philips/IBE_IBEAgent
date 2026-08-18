# -----------------------------------------------------------------------------
# Start-TcpReceiver.ps1 - external downstream system (TCP / MLLP).
#
# This is the system the IBE Agent DELIVERS to over a TCP outbound comm point.
# The agent connects out to it; this script listens, prints every message it
# receives, and returns an MLLP acknowledgement. A reply is required whenever
# the agent's TCP outbound comm point has ExpectReply = true (which is what
# feeds Enhanced-ack and Response contracts).
#
# Run this in its own terminal and leave it running for the demonstration.
# Stop it with Ctrl+C.
# -----------------------------------------------------------------------------
[CmdletBinding()]
param(
    [int]$Port = 5201,
    [string]$AckPayload = 'MSA|AA|RECEIVED-BY-TCP-DOWNSTREAM',
    [bool]$SendAck = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

$component = 'tcp-downstream'
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
$listener.Start()

Write-DemoLog -Component $component -Level INFO -Message "Downstream TCP system listening on 127.0.0.1:$Port."
Write-DemoLog -Component $component -Level INFO -Message "Waiting for the IBE Agent to deliver messages. Press Ctrl+C to stop."

try {
    while ($true) {
        if (-not $listener.Pending()) {
            Start-Sleep -Milliseconds 50
            continue
        }

        $client = $listener.AcceptTcpClient()
        $client.NoDelay = $true   # disable Nagle: MLLP ack else stalls ~40ms (Nagle + delayed-ACK)
        $remote = $client.Client.RemoteEndPoint.ToString()
        Write-DemoLog -Component $component -Level INFO -Message "IBE Agent connected from $remote."

        try {
            $stream = $client.GetStream()
            while ($true) {
                $frame = Read-MllpFrame -Stream $stream -ReadTimeoutMs 2000
                if ($frame.Status -ne 'Frame') { break }

                $text = Get-Utf8String -Bytes $frame.Payload
                Write-DemoLog -Component $component -Level RECV -Message "Message delivered ($($frame.Payload.Length) bytes):"
                Write-Hl7 -Message $text

                if ($SendAck) {
                    $replyBytes = New-MllpFrame -Payload (Get-Utf8Bytes -Text $AckPayload)
                    $stream.Write($replyBytes, 0, $replyBytes.Length)
                    $stream.Flush()
                    Write-DemoLog -Component $component -Level ACK -Message "Returned acknowledgement: $AckPayload"
                }
            }
        }
        catch {
            Write-DemoLog -Component $component -Level WARN -Message "Connection ended: $($_.Exception.Message)"
        }
        finally {
            $client.Close()
        }
    }
}
finally {
    $listener.Stop()
    Write-DemoLog -Component $component -Level INFO -Message "Downstream TCP system stopped."
}
