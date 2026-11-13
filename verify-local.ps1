#requires -version 5.1
<#
.SYNOPSIS
  Verifies the local GovBid Pro environment: containers, database, migrations and app health.
.DESCRIPTION
  Read-only. Prints one line per check and exits with code 1 if any check fails.
  It does not test sign-in, tenant isolation or business workflows; the automated test suites
  cover those (see README.md, "Build and test").
.EXAMPLE
  .\verify-local.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts/LocalDev.Common.ps1')

$script:Failures = 0
function Write-Check([string]$Name, [bool]$Ok, [string]$Detail) {
    if ($Ok) {
        Write-Host ('[PASS] {0}{1}' -f $Name, $(if ($Detail) { ' - ' + $Detail } else { '' })) -ForegroundColor Green
    } else {
        $script:Failures++
        Write-Host ('[FAIL] {0}{1}' -f $Name, $(if ($Detail) { ' - ' + $Detail } else { '' })) -ForegroundColor Red
    }
}

Assert-DockerReady
Write-Check 'Docker engine and Compose v2' $true ''

# --- Git hygiene ------------------------------------------------------------------------------
if (Get-Command git -ErrorAction SilentlyContinue) {
    $tracked = Invoke-Native -FilePath 'git' -Arguments @('-C', $RepoRoot, 'ls-files', '--error-unmatch', '.env')
    Write-Check '.env is not tracked by Git' ($tracked.ExitCode -ne 0) ''
}

# --- Containers -------------------------------------------------------------------------------
$running = Get-RunningServices
Write-Check 'Database container is running' ($running -contains $DbService) ''
Write-Check 'Web container is running' ($running -contains $WebService) ''

# --- Database ---------------------------------------------------------------------------------
$dbUp = $false
if ($running -contains $DbService) {
    $isReady = Invoke-Compose -Arguments @('exec', '-T', $DbService, 'pg_isready', '-U', $DbUser, '-d', $DbName)
    $dbUp = ($isReady.ExitCode -eq 0)
    Write-Check 'PostgreSQL accepts connections' $dbUp (($isReady.Output | Select-Object -First 1) -as [string])
}

if ($dbUp) {
    # Migrations in source: every migration has a <id>.Designer.cs file next to it.
    $migrationDirectory = Join-Path $RepoRoot 'src/GovBidPro.Infrastructure/Persistence/Migrations'
    $expected = @(Get-ChildItem -LiteralPath $migrationDirectory -Filter '*.Designer.cs' | ForEach-Object { $_.Name -replace '\.Designer\.cs$', '' } | Sort-Object)

    $applied = Invoke-DbQuery -Sql 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";'
    if ($applied.ExitCode -ne 0) {
        Write-Check 'EF Core migrations are applied' $false 'the migration history table could not be read; the app has not migrated this database yet'
    } else {
        $appliedIds = @($applied.Output | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        $pending = @($expected | Where-Object { $appliedIds -notcontains $_ })
        if ($pending.Count -eq 0) {
            Write-Check 'EF Core migrations are applied' $true ('{0} of {1} applied' -f $appliedIds.Count, $expected.Count)
        } else {
            Write-Check 'EF Core migrations are applied' $false ('pending: ' + ($pending -join ', '))
        }
    }

    $role = Invoke-DbQuery -Sql 'SELECT count(*) FROM "AspNetRoles" WHERE "NormalizedName" = ''PLATFORMADMIN'';'
    Write-Check 'Startup initialization completed (platform role exists)' (($role.ExitCode -eq 0) -and (($role.Output -join '').Trim() -eq '1')) ''

    $fixtures = Invoke-DbQuery -Sql 'SELECT count(*) FROM opportunities;'
    if ($fixtures.ExitCode -eq 0) {
        $count = ($fixtures.Output -join '').Trim()
        Write-Check 'Demo fixture notices are loaded' ($count -match '^\d+$' -and [int]$count -gt 0) ('{0} notices (fictional demo data, not SAM.gov)' -f $count)
    } else {
        Write-Check 'Demo fixture notices are loaded' $false 'the opportunities table could not be read'
    }
}

# --- Application ------------------------------------------------------------------------------
if ($running -contains $WebService) {
    $baseUrl = Get-WebBaseUrl
    $live = Test-HttpOk -Uri ($baseUrl + '/health/live')
    Write-Check ('Liveness  {0}/health/live' -f $baseUrl) $live.Ok $live.Detail
    $ready = Test-HttpOk -Uri ($baseUrl + '/health/ready')
    Write-Check ('Readiness {0}/health/ready (includes a database check)' -f $baseUrl) $ready.Ok $ready.Detail
    $homePage = Test-HttpOk -Uri ($baseUrl + '/')
    Write-Check ('Home page {0}/' -f $baseUrl) $homePage.Ok $homePage.Detail
}

Write-Host ''
if ($script:Failures -gt 0) {
    Write-Host ('{0} check(s) failed. View logs with "docker compose logs --tail 100 web" and see the troubleshooting section of docs/LOCAL_SETUP.md.' -f $script:Failures) -ForegroundColor Red
    exit 1
}
Write-Host 'All checks passed.' -ForegroundColor Green
