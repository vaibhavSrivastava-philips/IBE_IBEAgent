# -----------------------------------------------------------------------------
# Send-FileMessage.ps1 - upstream File comm point (a "file drop").
#
# Stands in for a real upstream system that feeds the IBE Agent over a File
# inbound endpoint: it writes one message into the agent's polled directory,
# atomically (staging temp -> move) so the poller never reads a partial file.
# A File source has no reply channel, so there is nothing to read back; the
# result only reports whether the drop succeeded.
# -----------------------------------------------------------------------------
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TargetDir,   # the agent's polled input directory
    [Parameter(Mandatory)][string]$Payload,     # the message (HL7, or a base64 blob envelope)
    [string]$FileName,                           # defaults to a unique name
    [string]$Extension = 'hl7'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')
. (Join-Path $PSScriptRoot '..\lib\FileCommon.ps1')

$result = [ordered]@{
    Transport = 'file'
    Sent      = $false
    Path      = $null
    Error     = $null
}

try {
    if (-not $FileName) {
        $FileName = 'msg-{0}.{1}' -f ([System.IO.Path]::GetRandomFileName().Replace('.', '')), $Extension
    }
    $result.Path = New-FileDrop -TargetDir $TargetDir -FileName $FileName -Content $Payload
    $result.Sent = $true
}
catch {
    $result.Error = $_.Exception.Message
}

[pscustomobject]$result
