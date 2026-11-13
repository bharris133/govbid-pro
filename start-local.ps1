#requires -version 5.1
<#
.SYNOPSIS
  Prepares .env and starts GovBid Pro locally with Docker Compose (PostgreSQL + web app).
.DESCRIPTION
  1. Checks that Docker Desktop is running.
  2. Creates .env from .env.example if needed and generates a random local database password
     (the value is never printed).
  3. Validates compose.yaml, builds the image and starts the containers.
  4. Waits until the app reports ready. In Development the app applies EF Core migrations and
     imports the labeled demo fixture notices when it starts.

  Local development only. Needs no Azure credentials and creates no Azure resources.
.PARAMETER NoBuild
  Start the existing image without rebuilding it.
.PARAMETER TimeoutSeconds
  How long to wait for the app to report ready. Default 240.
.EXAMPLE
  .\start-local.ps1
.EXAMPLE
  .\start-local.ps1 -NoBuild
#>
[CmdletBinding()]
param(
    [switch]$NoBuild,
    [ValidateRange(10, 3600)][int]$TimeoutSeconds = 240
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts/LocalDev.Common.ps1')

Assert-DockerReady

# --- .env -------------------------------------------------------------------------------------
if (-not (Test-Path -LiteralPath $EnvFile -PathType Leaf)) {
    if (-not (Test-Path -LiteralPath $EnvExampleFile -PathType Leaf)) {
        throw '.env.example is missing from the repository root. Restore it with: git checkout -- .env.example'
    }
    Copy-Item -LiteralPath $EnvExampleFile -Destination $EnvFile
    Write-Host 'Created .env from .env.example.' -ForegroundColor Cyan
}

$password = Get-EnvFileValue -Name $PasswordVariable
if ([string]::IsNullOrWhiteSpace($password) -or $password -eq $PasswordPlaceholder) {
    if (Test-DbVolumeExists) {
        # PostgreSQL only reads POSTGRES_PASSWORD when it first creates the data volume. A new
        # password would not match the existing database, so stop instead of breaking sign-in.
        throw ("A local database volume ({0}) already exists, but .env has no {1}. Either put the password that database was created with into .env, or run .\reset-local.ps1 to delete the local data and start clean." -f $DbVolume, $PasswordVariable)
    }
    Set-EnvFileValue -Name $PasswordVariable -Value (New-LocalSecret)
    Write-Host ('Generated a random {0} in .env (value not shown).' -f $PasswordVariable) -ForegroundColor Green
}
$password = $null

# --- Compose ----------------------------------------------------------------------------------
Write-Host 'Validating Docker Compose configuration...'
$config = Invoke-Compose -Arguments @('config', '--quiet')
if ($config.ExitCode -ne 0) {
    $config.Output | Out-Host
    throw 'Docker Compose configuration is invalid. Check .env against .env.example.'
}

Write-Host 'Starting containers (the first build downloads base images and can take several minutes)...'
$upArguments = @('up', '-d')
if (-not $NoBuild) { $upArguments += '--build' }
if ((Invoke-ComposeStreaming -Arguments $upArguments) -ne 0) {
    throw 'Docker Compose could not start the containers. See the output above, or run: docker compose logs'
}

# --- Wait for readiness -----------------------------------------------------------------------
$baseUrl = Get-WebBaseUrl
$readyUrl = $baseUrl + '/health/ready'
Write-Host ('Waiting for {0} (up to {1} seconds)...' -f $readyUrl, $TimeoutSeconds)
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$ready = $false
while ((Get-Date) -lt $deadline) {
    if ((Test-HttpOk -Uri $readyUrl -TimeoutSec 5).Ok) { $ready = $true; break }
    if ((Get-RunningServices) -notcontains $WebService) {
        # The web container exited (for example a database password mismatch). Do not keep waiting.
        break
    }
    Start-Sleep -Seconds 3
}

Write-Host ''
Invoke-ComposeStreaming -Arguments @('ps') | Out-Null
Write-Host ''
if (-not $ready) {
    Write-Host 'Last lines of the web log:' -ForegroundColor Yellow
    Invoke-ComposeStreaming -Arguments @('logs', '--tail', '40', $WebService) | Out-Null
    throw 'GovBid Pro did not become ready. See the log above and the troubleshooting section of docs/LOCAL_SETUP.md.'
}

Write-Host ('GovBid Pro is running: {0}' -f $baseUrl) -ForegroundColor Green
Write-Host 'Next steps:'
Write-Host '  .\verify-local.ps1                 check containers, database, migrations and health'
Write-Host ('  Open {0}/Identity/Account/Register to create your account, then create your company.' -f $baseUrl)
Write-Host '  .\init-local-admin.ps1 -Email <you> (optional) grant your account the platform administrator role'
