# Local setup on Windows

This guide runs GovBid Pro on a Windows 10/11 machine with Docker Desktop. It needs no Azure account, no Azure credentials, no SAM.gov key and no AI key. The local app shows **fictional demo fixture notices**, labeled as such; AI stays off.

Run every command in **PowerShell** from the repository root unless a step says otherwise. Windows PowerShell 5.1 and PowerShell 7 both work.

| Script | What it does |
|---|---|
| `setup-windows.ps1` | Reports which tools are installed. With `-Install`, installs missing ones through winget. |
| `start-local.ps1` | Creates `.env` with a random database password, builds the image, starts PostgreSQL and the web app, waits until the app is ready. |
| `verify-local.ps1` | Checks containers, database connectivity, applied migrations, startup initialization and health endpoints. Read-only. |
| `init-local-admin.ps1` | Grants or revokes the platform administrator role for an account you registered locally. |
| `reset-local.ps1` | Deletes the local containers, database and uploaded documents after confirmation. |

Do not use the local environment for FCI, CUI, classified or any real customer data.

## 1. Install the prerequisites

| Tool | Needed for | Notes |
|---|---|---|
| Git | Everything | |
| Docker Desktop with the WSL 2 backend | Everything | Linux containers. Docker Desktop licensing terms apply to larger organizations. |
| .NET SDK 10.0.x | Only to build, test or run EF Core commands on Windows itself | Version is pinned in `global.json`. |
| Node.js 22 with npm | Only to compile TypeScript or run browser tests on Windows itself | Version is pinned in `.nvmrc`. |

You cannot run the scripts before you have the repository, so if Git is missing install it first (`winget install --exact --id Git.Git`), then clone (section 2) and continue here.

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass   # allows these scripts in this window only
.\setup-windows.ps1            # report only, changes nothing
.\setup-windows.ps1 -Install   # install what is missing through winget
```

`-Install` runs `winget install` for `Git.Git`, `Docker.DockerDesktop`, `Microsoft.DotNet.SDK.10` and `OpenJS.NodeJS.22`. Read each prompt before approving it. Afterwards:

1. Restart Windows if an installer asks for it.
2. If WSL is not set up, open PowerShell **as administrator**, run `wsl --install`, and restart. Virtualization must be enabled in the BIOS/UEFI.
3. Start Docker Desktop and wait until it shows "Engine running". In Settings > General, "Use the WSL 2 based engine" must be on.
4. Open a **new** PowerShell window (so `PATH` is refreshed) and run `.\setup-windows.ps1` again to confirm.

## 2. Clone the repository and check out the branch

```powershell
cd C:\src                                    # any folder you like; avoid OneDrive-synced folders
git clone https://github.com/bharris133/govbid-pro.git
cd govbid-pro
git checkout feature/p02-local-setup
```

Once this work is merged, check out the branch that contains it instead (the scripts exist on any branch that has `start-local.ps1` in the root).

## 3. Prepare local environment variables

You normally do nothing here: `start-local.ps1` creates `.env` from `.env.example` and replaces the password placeholder with a random 48-character value, which it never prints.

To prepare it by hand instead:

```powershell
Copy-Item .env.example .env
notepad .env
```

| Variable | Default | Purpose |
|---|---|---|
| `GOVBID_LOCAL_DB_PASSWORD` | generated | Password of the local PostgreSQL container. Use only letters, digits, `-` and `_`. |
| `GOVBID_WEB_PORT` | `8080` | Host port of the web app (bound to `127.0.0.1`). |
| `GOVBID_DB_PORT` | `5432` | Host port of PostgreSQL (bound to `127.0.0.1`). |

`.env` is ignored by Git. Never commit it, and never paste real credentials, Azure keys, SAM.gov keys or AI keys into `.env.example`, an issue, a pull request or a chat.

PostgreSQL stores the password in its data volume the first time it starts. Changing `GOVBID_LOCAL_DB_PASSWORD` afterwards does not change the database password; use `.\reset-local.ps1` to start clean.

All other development settings (local file storage, fixture import, HTTP cookies for localhost, malware-scan bypass) come from `src/GovBidPro.Web/appsettings.Development.json` and apply only when `ASPNETCORE_ENVIRONMENT` is `Development`, which `compose.yaml` sets. The app refuses to start with those settings in any other environment.

## 4. Start GovBid Pro

```powershell
.\start-local.ps1
```

The first run downloads base images and compiles the app inside Docker (the TypeScript client is compiled in a Node stage, the .NET solution in an SDK stage), so allow several minutes. Later runs are much faster. Use `.\start-local.ps1 -NoBuild` to start without rebuilding.

### Database migrations

In Development the web app applies the reviewed EF Core migrations itself every time it starts (`Database:ApplyMigrationsOnStartup` in `appsettings.Development.json`), then creates the platform role and imports the demo fixture notices. Starting the app is therefore the repeatable way to migrate the local database, and `verify-local.ps1` confirms that every migration in `src/GovBidPro.Infrastructure/Persistence/Migrations` is recorded in the database.

To apply or inspect migrations from Windows instead (needs the .NET SDK, and the database container running):

```powershell
$line = Get-Content .env | Where-Object { $_ -match '^GOVBID_LOCAL_DB_PASSWORD=' }
$env:ConnectionStrings__GovBidPro = 'Host=localhost;Port=5432;Database=govbidpro;Username=govbid;Password=' + ($line -replace '^GOVBID_LOCAL_DB_PASSWORD=', '')
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool restore
dotnet ef migrations list -p src/GovBidPro.Infrastructure -s src/GovBidPro.Web
dotnet ef database update -p src/GovBidPro.Infrastructure -s src/GovBidPro.Web
```

Use the value of `GOVBID_DB_PORT` instead of `5432` if you changed it. The environment variables last only for that PowerShell window. The same two variables let you run the app on Windows without the web container: `dotnet run --project src/GovBidPro.Web --urls http://localhost:5080` (after `npm ci` and `npm run build:client`).

## 5. Open the application and create the first administrator

Open <http://localhost:8080> (or the port in `GOVBID_WEB_PORT`).

**Company administrator.** No password is seeded anywhere. You create the first account yourself:

1. Go to <http://localhost:8080/Identity/Account/Register> and register with an email address and a password of at least 12 characters that you choose. Email confirmation is off in Development, so no mail server is needed.
2. Open **Opportunities**. You are sent to onboarding; enter a company name. The account that creates a company becomes its `CompanyAdmin`.

Use a password you do not use anywhere else. The app stores only its hash, in your local database.

**Platform administrator (optional).** Operator endpoints such as `/api/v1/admin/imports` need the separate `PlatformAdmin` role *and* a sign-in with two-factor authentication. Grant the role to the account you registered:

```powershell
.\init-local-admin.ps1 -Email you@example.com
```

Then turn on two-factor authentication at <http://localhost:8080/Identity/Account/Manage/TwoFactorAuthentication> (add the shown key to an authenticator app), sign out, and sign in again with a code. To remove the role: `.\init-local-admin.ps1 -Email you@example.com -Revoke`.

The script never handles a password. It links an existing account to the role by running SQL inside this project's PostgreSQL container, so it cannot reach any hosted database. Because it writes to the database directly, the change does not appear in the application's audit log. It is a local development convenience and is not the production procedure for appointing platform administrators.

## 6. Verify the application and database

```powershell
.\verify-local.ps1
```

It prints `[PASS]` or `[FAIL]` for each check and exits with code 1 on any failure:

- Docker engine and Compose v2 are available, and `.env` is not tracked by Git.
- The `db` and `web` containers are running, and PostgreSQL accepts connections.
- Every EF Core migration in the source tree is applied.
- Startup initialization finished (the platform role exists) and the demo fixture notices are loaded.
- `GET /health/live`, `GET /health/ready` (includes a database check) and the home page return HTTP 200.

It does not test sign-in, tenant isolation or business workflows. The automated suites do (README, "Build and test"). To run the browser smoke test against the Docker environment (needs Node.js 22):

```powershell
npm ci
npx playwright install chromium
$env:GOVBID_BASE_URL = 'http://localhost:8080'
npm run test:browser
```

## 7. View logs

```powershell
docker compose logs -f web            # follow the application log (Ctrl+C stops following, not the app)
docker compose logs --tail 100 web    # last 100 lines
docker compose logs db                # PostgreSQL
docker compose ps                     # container status and ports
```

## 8. Stop and restart

```powershell
docker compose stop        # stop the containers; data is kept
.\start-local.ps1 -NoBuild # start again without rebuilding
.\start-local.ps1          # start again and rebuild (use after pulling new code)
docker compose restart web # restart only the web app
docker compose down        # remove the containers and network; data volumes are kept
```

Never add `--volumes` (or `-v`) to `docker compose down` unless you intend to delete the local database; use the reset script instead so you get a confirmation prompt.

## 9. Reset the local environment safely

```powershell
.\reset-local.ps1
```

After you type `RESET`, it removes the containers, network and volumes of this Compose project only (`govbid-pro`). That permanently deletes the local database, all local accounts and locally uploaded documents. It does not touch other Docker projects, your source code, Git history or anything in Azure.

| Option | Effect |
|---|---|
| `-RemoveEnv` | Also delete `.env`, so the next start generates a new password. |
| `-RemoveImages` | Also delete the locally built image, forcing a full rebuild. |
| `-Force` | Skip the confirmation prompt. |

Then run `.\start-local.ps1` and register your account again.

## 10. Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `running scripts is disabled on this system` | Run `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass` in the same window. If the files were downloaded as a ZIP rather than cloned, also run `Get-ChildItem *.ps1, scripts\*.ps1 \| Unblock-File`. |
| `The Docker engine is not running` | Start Docker Desktop and wait for "Engine running". If it will not start, run `wsl --status` and `wsl --update`, and check that virtualization is enabled. |
| `docker` or `dotnet` is not recognized right after installing | Open a new PowerShell window. |
| `Bind for 127.0.0.1:8080 failed: port is already allocated` (or 5432) | Another program uses the port. Set `GOVBID_WEB_PORT` or `GOVBID_DB_PORT` in `.env` to a free port and run `.\start-local.ps1 -NoBuild`. Find the owner with `Get-NetTCPConnection -LocalPort 8080`. |
| `A local database volume ... already exists, but .env has no GOVBID_LOCAL_DB_PASSWORD` | `.env` was deleted but the database remains. Put the old password back in `.env`, or run `.\reset-local.ps1`. A database created with plain `docker compose up` before `.env` existed uses the compose fallback password `govbid_local_dev`. |
| Web container exits; log shows `28P01: password authentication failed for user "govbid"` | The password in `.env` differs from the one the database volume was created with. Restore the original value or run `.\reset-local.ps1`. |
| `GovBid Pro did not become ready` | Read the log the script printed, or `docker compose logs --tail 100 web`. A slow first start can be retried with `.\start-local.ps1 -NoBuild -TimeoutSeconds 600`. |
| Image build fails downloading packages (`NU1301`, `npm ERR! network`, certificate errors) | A proxy or TLS-inspecting firewall is blocking nuget.org, npmjs.org or mcr.microsoft.com from inside Docker. Try another network or configure the proxy in Docker Desktop > Settings > Resources > Proxies. |
| Build fails with `no space left on device` | Free space with `docker system prune` (removes unused images and build cache, not volumes). |
| Sign-in says the account is locked | Five failed attempts lock an account for 15 minutes. Wait, or reset the environment. |
| Sign-in does not stick | Use `http://localhost:<port>` exactly. The local cookie is for plain HTTP on localhost; do not expose the port to other machines. |
| Platform administrator endpoint returns 403 | Run `init-local-admin.ps1`, enable two-factor authentication, then sign out and in with a code. |
| `dotnet ef` cannot connect | The database container must be running, and the connection-string variables from section 4 must be set in the same window. |
| Edited code but the app did not change | Rebuild: `.\start-local.ps1` (without `-NoBuild`). |

Running `docker compose up --build -d` directly still works and is what the scripts call. Without a `.env` file it falls back to the fixed local password `govbid_local_dev`, so prefer `start-local.ps1`, which generates a random one.
