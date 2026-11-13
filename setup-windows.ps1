#requires -version 5.1
<#
.SYNOPSIS
  Checks (and optionally installs) the Windows tools needed to run GovBid Pro locally.
.DESCRIPTION
  Without parameters this script only reports what is installed. With -Install it installs
  missing tools through winget. Installs can ask for administrator approval or a restart.

  Needed to run the app in Docker: Git, Docker Desktop with the WSL 2 backend.
  Needed only to build, test or run EF Core commands on Windows itself: .NET 10 SDK, Node.js 22.

  The script never creates cloud resources, never asks for credentials and never stores any.
.PARAMETER Install
  Install missing tools with winget.
.EXAMPLE
  .\setup-windows.ps1
.EXAMPLE
  .\setup-windows.ps1 -Install
#>
[CmdletBinding()]
param([switch]$Install)

$ErrorActionPreference = 'Stop'

# Versions follow the repository: global.json (.NET SDK 10.0.x) and .nvmrc / package.json (Node 22).
$packages = @(
    @{ Name = 'Git';                      Command = 'git';    Id = 'Git.Git';                 Required = $true },
    @{ Name = 'Docker Desktop';           Command = 'docker'; Id = 'Docker.DockerDesktop';    Required = $true },
    @{ Name = '.NET 10 SDK';              Command = 'dotnet'; Id = 'Microsoft.DotNet.SDK.10'; Required = $false },
    @{ Name = 'Node.js 22 (with npm)';    Command = 'node';   Id = 'OpenJS.NodeJS.22';        Required = $false }
)

function Get-NativeOutput([string]$FilePath, [string[]]$Arguments) {
    # Windows PowerShell 5.1 raises errors for redirected native stderr under 'Stop'.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { return @(& $FilePath @Arguments 2>$null | ForEach-Object { "$_" }) } catch { return @() } finally { $ErrorActionPreference = $previous }
}

function Test-Package([hashtable]$Package) {
    if (-not (Get-Command $Package.Command -ErrorAction SilentlyContinue)) { return $false }
    if ($Package.Command -eq 'dotnet') {
        $sdks = Get-NativeOutput 'dotnet' @('--list-sdks')
        return (@($sdks | Where-Object { $_ -match '^10\.0\.' }).Count -gt 0)
    }
    if ($Package.Command -eq 'node') {
        if (-not (Get-Command npm -ErrorAction SilentlyContinue)) { return $false }
        $version = (Get-NativeOutput 'node' @('--version')) -join ''
        return ($version -match '^v22\.')
    }
    return $true
}

if ($Install -and -not (Get-Command winget -ErrorAction SilentlyContinue)) {
    throw 'winget was not found. Install or update "App Installer" from the Microsoft Store, then run this script again.'
}

$missingRequired = @()
$missingOptional = @()
foreach ($package in $packages) {
    if (Test-Package $package) {
        Write-Host ('[OK]      {0}' -f $package.Name) -ForegroundColor Green
        continue
    }
    if ($package.Required) {
        $missingRequired += $package.Name
        Write-Host ('[MISSING] {0} (required)' -f $package.Name) -ForegroundColor Red
    } else {
        $missingOptional += $package.Name
        Write-Host ('[MISSING] {0} (only needed to build, test or run EF Core commands outside Docker)' -f $package.Name) -ForegroundColor Yellow
    }
    if ($Install) {
        Write-Host ('Installing {0} with winget ({1})...' -f $package.Name, $package.Id)
        & winget install --exact --id $package.Id --source winget --accept-package-agreements --accept-source-agreements
        if ($LASTEXITCODE -ne 0) {
            Write-Warning ('winget returned exit code {0} for {1}. Read its output above, then run this script again.' -f $LASTEXITCODE, $package.Name)
        }
    }
}

# WSL 2 is the backend Docker Desktop uses for Linux containers on Windows.
if (Get-Command wsl -ErrorAction SilentlyContinue) {
    Write-Host ''
    Write-Host 'WSL status:'
    & wsl --status
    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'WSL is not set up. In an administrator PowerShell run "wsl --install", restart Windows, then start Docker Desktop.'
    }
} else {
    Write-Warning 'The wsl command was not found. Docker Desktop needs WSL 2. In an administrator PowerShell run "wsl --install" and restart Windows.'
}

# Docker engine check only makes sense once the CLI exists.
if (Get-Command docker -ErrorAction SilentlyContinue) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & docker info --format '{{.ServerVersion}}' > $null 2> $null; $engineExit = $LASTEXITCODE } finally { $ErrorActionPreference = $previous }
    if ($engineExit -eq 0) {
        Write-Host '[OK]      Docker engine is running' -ForegroundColor Green
    } else {
        Write-Warning 'Docker is installed but the engine is not running. Start Docker Desktop and wait until it reports "Engine running".'
    }
}

Write-Host ''
if ($Install) {
    Write-Host 'Install attempt finished. Restart Windows if an installer asked for it, open a new PowerShell window so PATH is refreshed, start Docker Desktop, then run .\setup-windows.ps1 again to confirm.' -ForegroundColor Cyan
    exit 0
}
if ($missingRequired.Count -gt 0) {
    Write-Host 'Required tools are missing. To install them run: .\setup-windows.ps1 -Install' -ForegroundColor Yellow
    exit 1
}
if ($missingOptional.Count -gt 0) {
    Write-Host 'You can run GovBid Pro in Docker now: .\start-local.ps1' -ForegroundColor Green
    Write-Host 'To also build and test on Windows itself, run: .\setup-windows.ps1 -Install' -ForegroundColor Yellow
    exit 0
}
Write-Host 'All tools found. Next: .\start-local.ps1' -ForegroundColor Green
