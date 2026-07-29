# -----------------------------------------------------------------------------
# Start-HttpReceiver.ps1 - external downstream system (HTTP).
#
# This is the system the IBE Agent DELIVERS to over an HTTP outbound comm point.
# The agent POSTs to it; this script listens, prints every message it receives,
# and returns a response body. For a Response-mode contract, the body returned
# here is what the agent relays back to the original source.
#
# Run this in its own terminal and leave it running for the demonstration.
# Stop it with Ctrl+C.
# -----------------------------------------------------------------------------
[CmdletBinding()]
param(
    [string]$Prefix = 'http://localhost:5202/ibe/',
    [string]$ResponseBody = 'MSA|AA|RECEIVED-BY-HTTP-DOWNSTREAM',
    [int]$StatusCode = 200
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

$component = 'http-downstream'
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add($Prefix)
$listener.Start()

Write-DemoLog -Component $component -Level INFO -Message "Downstream HTTP system listening on $Prefix."
Write-DemoLog -Component $component -Level INFO -Message "Waiting for the IBE Agent to deliver messages. Press Ctrl+C to stop."

try {
    while ($true) {
        $context = $listener.GetContext()   # blocks until a request arrives
        try {
            $reader = New-Object System.IO.StreamReader($context.Request.InputStream, [System.Text.Encoding]::UTF8)
            $body = $reader.ReadToEnd()
            $reader.Dispose()

            Write-DemoLog -Component $component -Level RECV -Message "Message delivered via $($context.Request.HttpMethod) $($context.Request.Url.AbsolutePath) ($($body.Length) bytes):"
            Write-Hl7 -Message $body

            $responseBytes = Get-Utf8Bytes -Text $ResponseBody
            $context.Response.StatusCode = $StatusCode
            $context.Response.ContentType = 'application/octet-stream'
            $context.Response.ContentLength64 = $responseBytes.Length
            $context.Response.OutputStream.Write($responseBytes, 0, $responseBytes.Length)
            Write-DemoLog -Component $component -Level ACK -Message "Returned HTTP $StatusCode with body: $ResponseBody"
        }
        catch {
            Write-DemoLog -Component $component -Level WARN -Message "Request handling failed: $($_.Exception.Message)"
        }
        finally {
            $context.Response.Close()
        }
    }
}
finally {
    $listener.Stop()
    $listener.Close()
    Write-DemoLog -Component $component -Level INFO -Message "Downstream HTTP system stopped."
}
