# -----------------------------------------------------------------------------
# Send-HttpMessage.ps1 - external upstream system (HTTP).
#
# This is the system that FEEDS the IBE Agent over an HTTP inbound comm point.
# It POSTs to the agent's HTTP input and, each time you press Enter, sends one
# HL7 message and shows the response the agent returns. Over HTTP the request is
# held open until the agent replies (acknowledgement or response payload) or,
# for a no-ack contract, until the agent releases it with HTTP 504.
#
# Run this in its own terminal. Press Enter to send a message; type Q then Enter
# to quit.
# -----------------------------------------------------------------------------
[CmdletBinding()]
param(
    [string]$Uri = 'http://localhost:5102/ibe/',
    [string]$MessageFile = (Join-Path $PSScriptRoot '..\messages\adt-a01.hl7'),
    [int]$TimeoutSeconds = 40
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

$component = 'http-upstream'
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)

Write-DemoLog -Component $component -Level INFO -Message "Ready to POST to the IBE Agent HTTP input at $Uri."
Write-DemoLog -Component $component -Level INFO -Message "Press Enter to send an HL7 message, or type Q then Enter to quit."

$sequence = 0
try {
    while ($true) {
        $entry = Read-Host 'Send'
        if ($entry -match '^\s*[qQ]') { break }

        $sequence++
        $controlId = 'DEMO{0:D4}-{1}' -f $sequence, (Get-Date).ToString('HHmmss')
        $message = New-Hl7MessageFromFile -Path $MessageFile -ControlId $controlId

        Write-DemoLog -Component $component -Level SENT -Message "Sending message (control id $($message.ControlId)):"
        Write-Hl7 -Message $message.Text

        try {
            $content = [System.Net.Http.ByteArrayContent]::new((Get-Utf8Bytes -Text $message.Text))
            $content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new('application/octet-stream')

            $response = $client.PostAsync($Uri, $content).GetAwaiter().GetResult()
            $body = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
            $status = [int]$response.StatusCode

            if ($status -ge 200 -and $status -lt 300) {
                Write-DemoLog -Component $component -Level ACK -Message "Agent replied HTTP ${status}:"
                if ($body.Length -gt 0) { Write-Hl7 -Message (Get-Utf8String -Bytes $body) }
                else { Write-DemoLog -Component $component -Level INFO -Message "(empty body)" }
            }
            elseif ($status -eq 504) {
                Write-DemoLog -Component $component -Level INFO -Message "Agent returned HTTP 504 - held then released (expected for a no-ack contract)."
            }
            else {
                Write-DemoLog -Component $component -Level WARN -Message "Agent returned HTTP $status."
            }
        }
        catch {
            $reason = $_.Exception.Message
            if ($_.Exception.InnerException) { $reason = $_.Exception.InnerException.Message }
            Write-DemoLog -Component $component -Level ERROR -Message "Send failed: $reason"
        }
    }
}
finally {
    $client.Dispose()
    Write-DemoLog -Component $component -Level INFO -Message "Upstream HTTP sender stopped."
}
