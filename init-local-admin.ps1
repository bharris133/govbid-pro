#requires -version 5.1
<#
.SYNOPSIS
  Grants (or revokes) the PlatformAdmin role for an account in the LOCAL development database.
.DESCRIPTION
  GovBid Pro has two kinds of administrator:

  * Company administrator: the account that creates a company during onboarding. No script is
    needed; register in the browser and create your company.
  * Platform administrator: the PlatformAdmin Identity role, used by operator endpoints such as
    /api/v1/admin/imports. Those endpoints also require a sign-in with two-factor authentication.

  This script handles the second kind. It never sees, sets or stores a password: you register
  the account yourself in the browser, where the app hashes the password you choose. The script
  then links that account to the PlatformAdmin role.

  It runs SQL inside the PostgreSQL container of this repository's Docker Compose project, so it
  cannot reach any hosted or shared database. The change is made directly in the database and is
  therefore not written to the application's audit log. Local development only.
.PARAMETER Email
  Email address of an account that is already registered in the local app.
.PARAMETER Revoke
  Remove the role instead of granting it.
.EXAMPLE
  .\init-local-admin.ps1 -Email you@example.com
.EXAMPLE
  .\init-local-admin.ps1 -Email you@example.com -Revoke
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Email,
    [switch]$Revoke
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts/LocalDev.Common.ps1')

$Email = $Email.Trim()
# Deliberately strict: plain ASCII addresses only. The value is also passed as a quoted psql variable.
if ($Email.Length -gt 254 -or $Email -notmatch '^[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}$') {
    throw 'Email must be a plain address such as you@example.com.'
}

Assert-DockerReady
if ((Get-RunningServices) -notcontains $DbService) {
    throw 'The local database container is not running. Run .\start-local.ps1 first.'
}

$baseUrl = Get-WebBaseUrl
$variables = @{ email = $Email }

$user = Invoke-DbQuery -Variables $variables -Sql 'SELECT "TwoFactorEnabled" FROM "AspNetUsers" WHERE "NormalizedEmail" = upper(:''email'');'
if ($user.ExitCode -ne 0) {
    $user.Output | Out-Host
    throw 'Could not read the local database. Run .\verify-local.ps1 to check that migrations are applied.'
}
$twoFactor = ($user.Output -join '').Trim()
if (-not $twoFactor) {
    Write-Host ('No local account exists for {0}.' -f $Email) -ForegroundColor Yellow
    Write-Host ('Register it first at {0}/Identity/Account/Register (choose your own password; at least 12 characters), then run this command again.' -f $baseUrl)
    exit 1
}

if ($Revoke) {
    $sql = @'
DELETE FROM "AspNetUserRoles" ur
USING "AspNetUsers" u, "AspNetRoles" r
WHERE ur."UserId" = u."Id" AND ur."RoleId" = r."Id"
  AND u."NormalizedEmail" = upper(:'email') AND r."NormalizedName" = 'PLATFORMADMIN';
'@
} else {
    $sql = @'
INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
SELECT u."Id", r."Id"
FROM "AspNetUsers" u CROSS JOIN "AspNetRoles" r
WHERE u."NormalizedEmail" = upper(:'email') AND r."NormalizedName" = 'PLATFORMADMIN'
ON CONFLICT DO NOTHING;
'@
}
$change = Invoke-DbQuery -Variables $variables -Sql $sql
if ($change.ExitCode -ne 0) {
    $change.Output | Out-Host
    throw 'The role change failed.'
}

$check = Invoke-DbQuery -Variables $variables -Sql @'
SELECT count(*) FROM "AspNetUserRoles" ur
JOIN "AspNetUsers" u ON u."Id" = ur."UserId"
JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
WHERE u."NormalizedEmail" = upper(:'email') AND r."NormalizedName" = 'PLATFORMADMIN';
'@
$hasRole = (($check.Output -join '').Trim() -eq '1')

if ($Revoke) {
    if ($hasRole) { throw 'The role is still assigned. Check the database log: docker compose logs db' }
    Write-Host ('{0} no longer has the PlatformAdmin role. Sign out and back in for it to take effect.' -f $Email) -ForegroundColor Green
    exit 0
}

if (-not $hasRole) {
    throw 'The PlatformAdmin role was not assigned. The role is created when the app starts; run .\start-local.ps1 and try again.'
}
Write-Host ('{0} has the PlatformAdmin role in the local database.' -f $Email) -ForegroundColor Green
Write-Host 'To use it:'
if ($twoFactor -ne 't') {
    Write-Host ('  1. Turn on two-factor authentication: {0}/Identity/Account/Manage/TwoFactorAuthentication' -f $baseUrl)
    Write-Host '     (add an authenticator app; platform administrator endpoints refuse sign-ins without it).'
    Write-Host '  2. Sign out, then sign in again with your authenticator code.'
} else {
    Write-Host '  Sign out, then sign in again with your authenticator code.'
}
