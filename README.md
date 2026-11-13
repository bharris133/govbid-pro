# GovBid Pro

Multi-tenant SaaS for commercial government contractors: find public opportunities, track notice details and deadlines, and prepare responses with a team.

**Status: P0 foundation.** Built from the v4 engineering handoff (owner decisions D-01 Azure, D-02 provisional retention, D-03 Azure-first AI strategy). Opportunity data is **fictional fixture data**; the official SAM.gov integration is not built or verified yet. AI is off. Nothing has been deployed. See [docs/production-gates.md](docs/production-gates.md) for everything that still needs owner, legal or security approval.

GovBid Pro does not submit proposals to any agency, does not certify compliance or eligibility, and holds no FedRAMP, CMMC or Section 508 certification. Release 1 must not be used for FCI, CUI or classified information.

## What P0 includes

- .NET 10 modular monolith: Domain, Application, Infrastructure, Web (ASP.NET Core API + Razor Pages + Identity).
- PostgreSQL via EF Core 10 with a reviewed initial migration.
- Strict TypeScript browser client compiled to ES modules (no hand-written JavaScript, no inline script, strict CSP).
- Tenant isolation: server-side tenant resolution, explicit tenant filters, fail-closed query filters, 404 on cross-tenant IDs, audited denials.
- Opportunities: notice type kept separate from response type, provenance and revisions, deadline time zone and original text, idempotent and resumable import, labeled fixture source.
- Saved searches (NAICS, agency, keywords, notice/response type, set-aside, state, dates).
- Documents: classification declarations, CUI/classified marking rejection before storage, file signature checks, private storage, authorized streaming downloads, soft delete/restore, quarantine.
- Configurable, versioned retention with an idempotent purge job (provisional D-02 values in configuration only).
- AI provider abstraction and fail-closed policy gate, disabled by default.
- Platform-admin endpoints that require MFA.
- Tests: unit, PostgreSQL integration, API security (WebApplicationFactory) and TypeScript Playwright browser smoke tests.
- CI: locked restores, format check, Release build with warnings as errors, migration drift check, tests, browser smoke, dependency audit, secret scan, container build + SBOM + Trivy, Bicep compile, CodeQL.
- Azure Bicep skeleton: Container Apps, PostgreSQL Flexible Server (private), Blob Storage (private, no shared keys), Key Vault, managed identity, Log Analytics/App Insights. Not deployed.
- ADRs, threat model, API contract, glossary, traceability matrix and runbook in [docs/](docs/).

## Prerequisites

- .NET SDK 10.0.1xx (`global.json`)
- Node.js 22 (`.nvmrc`) and npm
- PostgreSQL 16 (or Docker)

On Windows, `.\setup-windows.ps1` checks these and can install them; see [docs/LOCAL_SETUP.md](docs/LOCAL_SETUP.md).

## Run locally

### Windows (Docker Desktop + WSL 2)

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\setup-windows.ps1            # report installed tools; add -Install to install missing ones with winget
.\start-local.ps1              # create .env with a random DB password, build, start, wait until ready
.\verify-local.ps1             # containers, database, migrations, health endpoints
# open http://localhost:8080, register an account, then create your company
```

No Azure credentials or deployed Azure resources are needed. The full guide covers prerequisites, environment variables, migrations, the first administrator account, logs, stop/restart, reset and troubleshooting: [docs/LOCAL_SETUP.md](docs/LOCAL_SETUP.md).

`.env` holds local-only settings and is ignored by Git; [.env.example](.env.example) lists the variables with placeholder values. Never commit real credentials.

### Any OS with Docker

```bash
docker compose up --build -d
# open http://localhost:8080, create an account, then create your company
```

Without a `.env` file Compose falls back to a fixed local-only database password; copy `.env.example` to `.env` and set your own to avoid that.

Without Docker, start PostgreSQL with the credentials in `src/GovBidPro.Web/appsettings.Development.json` (local-only), then:

```bash
npm ci
npm run build:client
dotnet run --project src/GovBidPro.Web --urls http://localhost:5080
```

Development mode applies migrations, imports the fixture notices, uses local file storage outside `wwwroot`, and bypasses malware scanning (rejected by startup validation in any other environment).

## Build and test

```bash
dotnet restore GovBidPro.sln --locked-mode
dotnet build GovBidPro.sln -c Release
npm ci
npm run typecheck
npm run build:client
dotnet test GovBidPro.sln                       # uses Testcontainers (Docker) for PostgreSQL
GOVBID_TEST_POSTGRES="Host=localhost;Username=...;Password=...;Database=postgres" dotnet test GovBidPro.sln   # or an existing server
npm run test:browser                            # needs a running app; GOVBID_BASE_URL defaults to http://localhost:5080
```

## Repository layout

```
src/GovBidPro.Domain          entities, enums, invariants
src/GovBidPro.Application     services, policies, ports
src/GovBidPro.Infrastructure  EF Core, Identity store, storage, sources, AI providers
src/GovBidPro.Web             API controllers, Razor Pages, middleware
ClientApp/src                 strict TypeScript (compiled to src/GovBidPro.Web/wwwroot/dist)
tests/                        unit, integration, API and browser tests
infra/                        Azure Bicep skeleton
docs/                         architecture, ADRs, gates, threat model, runbook, local setup
*.ps1, scripts/               Windows local development scripts (see docs/LOCAL_SETUP.md)
```
