# Shared helpers for the GovBid Pro local development scripts (start-local.ps1, verify-local.ps1,
# init-local-admin.ps1, reset-local.ps1). Dot-source this file; do not run it directly.
#
# Local development only. Nothing here talks to Azure or needs Azure credentials.
# Keep this file ASCII-only so Windows PowerShell 5.1 reads it correctly without a BOM.

$script:RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$script:ComposeFile = Join-Path $script:RepoRoot 'compose.yaml'
$script:EnvFile = Join-Path $script:RepoRoot '.env'
$script:EnvExampleFile = Join-Path $script:RepoRoot '.env.example'

# Names that must match compose.yaml.
$script:ComposeProject = 'govbid-pro'
$script:DbService = 'db'
$script:WebService = 'web'
$script:DbUser = 'govbid'
$script:DbName = 'govbidpro'
$script:DbVolume = 'govbid-pro_db-data'
$script:PasswordVariable = 'GOVBID_LOCAL_DB_PASSWORD'
$script:PasswordPlaceholder = 'replace-me-start-local-generates-this'

function Invoke-Native {
    <#
    .SYNOPSIS
      Runs a native command, captures stdout and stderr as text, and returns the exit code.
    .DESCRIPTION
      Windows PowerShell 5.1 turns redirected native stderr into terminating errors when
      $ErrorActionPreference is 'Stop'. This wrapper relaxes the preference for the call only.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$InputText
    )
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        if ($PSBoundParameters.ContainsKey('InputText')) {
            $output = $InputText | & $FilePath @Arguments 2>&1 | ForEach-Object { "$_" }
        } else {
            $output = & $FilePath @Arguments 2>&1 | ForEach-Object { "$_" }
        }
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
    return [pscustomobject]@{ ExitCode = $code; Output = @($output) }
}

function Get-ComposeArguments {
    param([string[]]$Arguments = @())
    return @('compose', '--project-directory', $script:RepoRoot, '-f', $script:ComposeFile) + $Arguments
}

function Invoke-Compose {
    # Captured output. Use for queries.
    param([string[]]$Arguments = @(), [string]$InputText)
    $all = Get-ComposeArguments -Arguments $Arguments
    if ($PSBoundParameters.ContainsKey('InputText')) {
        return Invoke-Native -FilePath 'docker' -Arguments $all -InputText $InputText
    }
    return Invoke-Native -FilePath 'docker' -Arguments $all
}

function Invoke-ComposeStreaming {
    # Output goes straight to the console (build progress, logs). Returns the exit code.
    param([string[]]$Arguments = @())
    $all = Get-ComposeArguments -Arguments $Arguments
    & docker @all | Out-Host
    return $LASTEXITCODE
}

function Assert-DockerReady {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker CLI not found. Install Docker Desktop (.\setup-windows.ps1 -Install) and reopen PowerShell.'
    }
    if ((Invoke-Native -FilePath 'docker' -Arguments @('info', '--format', '{{.ServerVersion}}')).ExitCode -ne 0) {
        throw 'The Docker engine is not running. Start Docker Desktop, make sure it uses Linux containers (WSL 2), and wait until it reports "Engine running".'
    }
    if ((Invoke-Native -FilePath 'docker' -Arguments @('compose', 'version')).ExitCode -ne 0) {
        throw 'Docker Compose v2 is required. Update Docker Desktop.'
    }
    if (-not (Test-Path -LiteralPath $script:ComposeFile -PathType Leaf)) {
        throw ('compose.yaml was not found at {0}. Run the script from a full clone of the repository.' -f $script:ComposeFile)
    }
}

function Get-EnvFileValue {
    param([Parameter(Mandatory = $true)][string]$Name)
    if (-not (Test-Path -LiteralPath $script:EnvFile -PathType Leaf)) { return $null }
    $pattern = '^\s*' + [regex]::Escape($Name) + '\s*=(.*)$'
    foreach ($line in [System.IO.File]::ReadAllLines($script:EnvFile)) {
        $match = [regex]::Match($line, $pattern)
        if ($match.Success) { return $match.Groups[1].Value.Trim() }
    }
    return $null
}

function Set-EnvFileValue {
    param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)][string]$Value)
    $lines = New-Object System.Collections.Generic.List[string]
    if (Test-Path -LiteralPath $script:EnvFile -PathType Leaf) {
        $lines.AddRange([System.IO.File]::ReadAllLines($script:EnvFile))
    }
    $pattern = '^\s*' + [regex]::Escape($Name) + '\s*='
    $replaced = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match $pattern) {
            $lines[$i] = $Name + '=' + $Value
            $replaced = $true
        }
    }
    if (-not $replaced) { $lines.Add($Name + '=' + $Value) }
    # UTF-8 without BOM: Docker Compose does not accept a BOM in .env.
    [System.IO.File]::WriteAllLines($script:EnvFile, $lines.ToArray(), (New-Object System.Text.UTF8Encoding($false)))
}

function New-LocalSecret {
    # 36 random bytes, URL-safe base64. Safe inside a connection string and a .env file.
    $bytes = New-Object byte[] 36
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Test-DbVolumeExists {
    return (Invoke-Native -FilePath 'docker' -Arguments @('volume', 'inspect', $script:DbVolume)).ExitCode -eq 0
}

function Get-RunningServices {
    $result = Invoke-Compose -Arguments @('ps', '--status', 'running', '--services')
    if ($result.ExitCode -ne 0) { return @() }
    return @($result.Output | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

function Get-WebBaseUrl {
    # Reads the published host port from the running container, so GOVBID_WEB_PORT is honored.
    $result = Invoke-Compose -Arguments @('port', $script:WebService, '8080')
    if ($result.ExitCode -eq 0) {
        foreach ($line in $result.Output) {
            $match = [regex]::Match($line, ':(\d+)\s*$')
            if ($match.Success) { return 'http://localhost:' + $match.Groups[1].Value }
        }
    }
    $port = Get-EnvFileValue -Name 'GOVBID_WEB_PORT'
    if (-not $port) { $port = '8080' }
    return 'http://localhost:' + $port
}

function Invoke-DbQuery {
    <#
    .SYNOPSIS
      Runs SQL inside the local Compose PostgreSQL container and returns the rows as text.
    .DESCRIPTION
      SQL is sent on stdin. Values go in -Variables and are referenced as :'name' in the SQL,
      so psql quotes them. Never build SQL by string concatenation with user input.
      This can only reach the database container of this Compose project.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Sql,
        [hashtable]$Variables = @{}
    )
    $arguments = @('exec', '-T', $script:DbService, 'psql', '-X', '-q', '-t', '-A', '-v', 'ON_ERROR_STOP=1', '-U', $script:DbUser, '-d', $script:DbName)
    foreach ($key in $Variables.Keys) {
        $arguments += @('-v', ('{0}={1}' -f $key, $Variables[$key]))
    }
    $arguments += @('-f', '-')
    return Invoke-Compose -Arguments $arguments -InputText $Sql
}

function Test-HttpOk {
    param([Parameter(Mandatory = $true)][string]$Uri, [int]$TimeoutSec = 10)
    try {
        $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec $TimeoutSec
        return [pscustomobject]@{ Ok = ([int]$response.StatusCode -eq 200); Detail = ('HTTP {0}' -f [int]$response.StatusCode) }
    } catch {
        return [pscustomobject]@{ Ok = $false; Detail = $_.Exception.Message }
    }
}
