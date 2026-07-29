# -----------------------------------------------------------------------------
# Send-TcpMessage.ps1 - external upstream system (TCP / MLLP).
#
# This is the system that FEEDS the IBE Agent over a TCP inbound comm point.
# It connects to the agent's TCP input and, each time you press Enter, sends one
# HL7 message and shows the acknowledgement the agent returns (if the contract
# is configured to send one).
#
# Run this in its own terminal. Press Enter to send a message; type Q then Enter
# to quit.
# -----------------------------------------------------------------------------
[CmdletBinding()]
param(
    [int]$Port = 5101,
    [string]$HostName = '127.0.0.1',
    [string]$MessageFile = (Join-Path $PSScriptRoot '..\messages\adt-a01.hl7'),
    [int]$AckTimeoutSeconds = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

$component = 'tcp-upstream'
Write-DemoLog -Component $component -Level INFO -Message "Connecting to the IBE Agent TCP input at ${HostName}:${Port} ..."

$client = New-Object System.Net.Sockets.TcpClient
$client.Connect($HostName, $Port)
$stream = $client.GetStream()
Write-DemoLog -Component $component -Level INFO -Message "Connected. Press Enter to send an HL7 message, or type Q then Enter to quit."

$sequence = 0
try {
    while ($true) {
        $entry = Read-Host 'Send'
        if ($entry -match '^\s*[qQ]') { break }

        $sequence++
        $controlId = 'DEMO{0:D4}-{1}' -f $sequence, (Get-Date).ToString('HHmmss')
        $message = New-Hl7MessageFromFile -Path $MessageFile -ControlId $controlId
        $bytes = Get-Utf8Bytes -Text $message.Text
        $frame = New-MllpFrame -Payload $bytes

        $stream.Write($frame, 0, $frame.Length)
        $stream.Flush()
        Write-DemoLog -Component $component -Level SENT -Message "Sent message (control id $($message.ControlId), $($bytes.Length) bytes):"
        Write-Hl7 -Message $message.Text

        $reply = Read-MllpFrame -Stream $stream -ReadTimeoutMs ($AckTimeoutSeconds * 1000)
        if ($reply.Status -eq 'Frame') {
            Write-DemoLog -Component $component -Level ACK -Message "Acknowledgement received from the agent:"
            Write-Hl7 -Message (Get-Utf8String -Bytes $reply.Payload)
        }
        else {
            Write-DemoLog -Component $component -Level INFO -Message "No acknowledgement within ${AckTimeoutSeconds}s (this is expected for a no-ack contract)."
        }
    }
}
finally {
    $client.Close()
    Write-DemoLog -Component $component -Level INFO -Message "Disconnected from the IBE Agent."
}
