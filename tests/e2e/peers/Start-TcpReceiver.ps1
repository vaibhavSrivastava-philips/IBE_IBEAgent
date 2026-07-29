# -----------------------------------------------------------------------------
# Start-TcpReceiver.ps1 - downstream TCP/MLLP comm point.
#
# Stands in for a real downstream system that the IBE Agent delivers to over a
# TCP outbound leg. It accepts MLLP-framed messages, records each one to a
# capture file, and (when enabled) replies with an MLLP-framed acknowledgement.
# A downstream MLLP reply is required for the agent's TcpOutboundEndpoint when
# ExpectReply is true, which is how Enhanced-ack and Response modes obtain their
# delivery outcome / response payload.
#
# Runs until the stop file appears or the process is terminated.
# -----------------------------------------------------------------------------
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$Port,
    [Parameter(Mandatory)][string]$CaptureFile,
    [Parameter(Mandatory)][string]$LogFile,
    [Parameter(Mandatory)][string]$StopFile,
    [string]$ReplyPayload = 'MSA|AA|TCP-DOWNSTREAM-OK',
    [bool]$ReplyEnabled = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

$component = "tcp-receiver:$Port"
Write-HarnessLog -Level INFO -Component $component -LogFile $LogFile -NoConsole `
    -Message "Downstream TCP receiver starting on 127.0.0.1:$Port (reply enabled: $ReplyEnabled)."

$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
$listener.Start()

try {
    while (-not (Test-Path -LiteralPath $StopFile)) {
        if (-not $listener.Pending()) {
            Start-Sleep -Milliseconds 50
            continue
        }

        $client = $listener.AcceptTcpClient()
        $remote = $client.Client.RemoteEndPoint.ToString()
        Write-HarnessLog -Level INFO -Component $component -LogFile $LogFile -NoConsole `
            -Message "Accepted connection from $remote."

        try {
            $stream = $client.GetStream()
            while ($true) {
                $frame = Read-MllpFrame -Stream $stream -ReadTimeoutMs 2000
                if ($frame.Status -ne 'Frame') { break }   # Timeout or Closed -> release connection

                $text = Get-Utf8String -Bytes $frame.Payload
                Add-CaptureRecord -Path $CaptureFile -Record @{
                    timestamp = (Get-Date).ToString('o')
                    transport = 'tcp'
                    port      = $Port
                    remote    = $remote
                    length    = $frame.Payload.Length
                    text      = $text
                }
                $firstLine = ($text -split "`r")[0]
                Write-HarnessLog -Level INFO -Component $component -LogFile $LogFile -NoConsole `
                    -Message "Received message ($($frame.Payload.Length) bytes). MSH: $firstLine"

                if ($ReplyEnabled) {
                    $replyBytes = New-MllpFrame -Payload (Get-Utf8Bytes -Text $ReplyPayload)
                    $stream.Write($replyBytes, 0, $replyBytes.Length)
                    $stream.Flush()
                    Write-HarnessLog -Level INFO -Component $component -LogFile $LogFile -NoConsole `
                        -Message "Sent MLLP acknowledgement: $ReplyPayload"
                }
            }
        }
        catch {
            Write-HarnessLog -Level WARN -Component $component -LogFile $LogFile -NoConsole `
                -Message "Connection handling ended: $($_.Exception.Message)"
        }
        finally {
            $client.Close()
        }
    }
}
finally {
    $listener.Stop()
    Write-HarnessLog -Level INFO -Component $component -LogFile $LogFile -NoConsole `
        -Message "Downstream TCP receiver stopped."
}
