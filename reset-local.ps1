#requires -version 5.1
<#
.SYNOPSIS
  Deletes the local GovBid Pro development environment so the next start is clean.
.DESCRIPTION
  Stops and removes the containers, network and volumes of this repository's Docker Compose
  project only (project name "govbid-pro"). That permanently deletes the LOCAL database,
  including every local account, and the LOCAL uploaded documents.

  It does not touch other Docker projects, your source code, Git history, or anything in Azure.
  By default .env is kept, so the next start reuses the same generated database password.
.PARAMETER RemoveEnv
  Also delete .env. The next .\start-local.ps1 creates a new one with a new random password.
.PARAMETER RemoveImages
  Also delete the locally built web image, forcing a full rebuild on the next start.
.PARAMETER Force
  Do not ask for confirmation.
.EXAMPLE
  .\reset-local.ps1
.EXAMPLE
  .\reset-local.ps1 -RemoveEnv -Force
#>
[CmdletBinding()]
param(
    [switch]$RemoveEnv,
    [switch]$RemoveImages,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts/LocalDev.Common.ps1')

Assert-DockerReady

if (-not $Force) {
    Write-Host ('This permanently deletes the local GovBid Pro database and uploaded documents (Compose project "{0}").' -f $ComposeProject) -ForegroundColor Yellow
    $answer = Read-Host 'Type RESET to continue'
    if ($answer -cne 'RESET') {
        Write-Host 'Nothing was changed.'
        exit 0
    }
}

$downArguments = @('down', '--volumes', '--remove-orphans')
if ($RemoveImages) { $downArguments += @('--rmi', 'local') }
if ((Invoke-ComposeStreaming -Arguments $downArguments) -ne 0) {
    throw 'Docker Compose could not remove the local environment. See the output above.'
}

if ($RemoveEnv -and (Test-Path -LiteralPath $EnvFile -PathType Leaf)) {
    Remove-Item -LiteralPath $EnvFile -Force
    Write-Host 'Deleted .env.'
}

Write-Host 'Local environment removed. Start again with: .\start-local.ps1' -ForegroundColor Green
