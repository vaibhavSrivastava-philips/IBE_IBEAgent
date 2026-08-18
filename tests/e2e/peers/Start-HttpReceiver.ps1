# -----------------------------------------------------------------------------
# Start-HttpReceiver.ps1 - downstream HTTP comm point.
#
# Stands in for a real downstream system that the IBE Agent delivers to over an
# HTTP outbound leg. It accepts POST requests, records each body to a capture
# file, and returns a configurable status code and response body. For Response
# mode the returned body is what the agent relays back to the original source.
#
# Runs until the stop file appears or the process is terminated.
# -----------------------------------------------------------------------------
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Prefix,          # e.g. http://localhost:19090/ibe/inbound/
    [Parameter(Mandatory)][string]$CaptureFile,
    [Parameter(Mandatory)][string]$LogFile,
    [Parameter(Mandatory)][string]$StopFile,
    [string]$ResponseBody = 'MSA|AA|HTTP-DOWNSTREAM-OK',
    [int]$StatusCode = 200
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

$component = 'http-receiver'
Write-HarnessLog -Level INFO -Component $component -LogFile $LogFile -NoConsole `
    -Message "Downstream HTTP receiver starting on $Prefix (status: $StatusCode)."

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add($Prefix)
$listener.Start()

try {
    while (-not (Test-Path -LiteralPath $StopFile)) {
        $contextTask = $listener.GetContextAsync()
        while (-not $contextTask.Wait(200)) {
            if (Test-Path -LiteralPath $StopFile) { break }
        }
        if (-not $contextTask.IsCompleted -or $contextTask.Status -ne [System.Threading.Tasks.TaskStatus]::RanToCompletion) { continue }

        $context = $contextTask.Result
        try {
            $reader = New-Object System.IO.StreamReader($context.Request.InputStream, [System.Text.Encoding]::UTF8)
            $body = $reader.ReadToEnd()
            $reader.Dispose()

            Add-CaptureRecord -Path $CaptureFile -Record @{
                timestamp = (Get-Date).ToString('o')
                transport = 'http'
                url       = $context.Request.Url.AbsolutePath
                method    = $context.Request.HttpMethod
                length    = $body.Length
                text      = $body
            }
            $firstLine = ($body -split "`r")[0]
            Write-HarnessLog -Level INFO -Component $component -LogFile $LogFile -NoConsole `
                -Message "Received $($context.Request.HttpMethod) $($context.Request.Url.AbsolutePath) ($($body.Length) bytes). MSH: $firstLine"

            $responseBytes = Get-Utf8Bytes -Text $ResponseBody
            $context.Response.StatusCode = $StatusCode
            $context.Response.ContentType = 'application/octet-stream'
            $context.Response.ContentLength64 = $responseBytes.Length
            $context.Response.OutputStream.Write($responseBytes, 0, $responseBytes.Length)
            Write-HarnessLog -Level INFO -Component $component -LogFile $LogFile -NoConsole `
                -Message "Responded $StatusCode with body: $ResponseBody"
        }
        catch {
            Write-HarnessLog -Level WARN -Component $component -LogFile $LogFile -NoConsole `
                -Message "Request handling failed: $($_.Exception.Message)"
        }
        finally {
            $context.Response.Close()
        }
    }
}
finally {
    $listener.Stop()
    $listener.Close()
    Write-HarnessLog -Level INFO -Component $component -LogFile $LogFile -NoConsole `
        -Message "Downstream HTTP receiver stopped."
}
