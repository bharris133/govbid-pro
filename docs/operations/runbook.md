# Operations runbook (skeleton)

> Production operation is blocked on OPS-01, OPS-02 and LEG-01. Sections marked **TBD** need owner decisions.

## Local development

Windows step-by-step guide (setup, start, verify, first administrator, reset, troubleshooting): [../LOCAL_SETUP.md](../LOCAL_SETUP.md).

```bash
docker compose up --build -d      # PostgreSQL + web at http://localhost:8080 (Development, fixture data)
```

Or run against a local PostgreSQL:

```bash
npm ci && npm run build:client
dotnet run --project src/GovBidPro.Web   # uses appsettings.Development.json
```

## Database migrations

- Migrations live in `src/GovBidPro.Infrastructure/Persistence/Migrations` and are reviewed in pull requests.
- Add one: `ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add <Name> -p src/GovBidPro.Infrastructure -s src/GovBidPro.Web -o Persistence/Migrations`
- CI fails if the model and migrations drift (`dotnet ef migrations has-pending-model-changes`).
- Production: apply with an idempotent script or migration bundle from the release pipeline (`dotnet ef migrations bundle --idempotent`), never `ApplyMigrationsOnStartup`. **TBD:** pipeline and approval step.

## Configuration and secrets (Azure)

- Secrets: Key Vault only, read by the container app through its managed identity.
- Non-secret settings: container app environment variables (see `infra/modules/containerapps.bicep`).
- Startup validation fails fast on: missing retention policies, local storage outside Development, AI enabled without AI-01 reference, development scan bypass outside Development, insecure cookies outside Development.

## Backups and restore

- PostgreSQL automated backups: retention parameterized (provisional 30 days). **TBD (OPS-02):** RTO/RPO, geo-redundancy, restore drill schedule and evidence.
- Blob soft delete aligned to the deleted-document recovery window.
- After a restore, re-run the retention purge so previously purged documents are deleted again (RET-03). **TBD:** documented re-delete procedure.

## Scheduled jobs

- Retention purge: `Retention:PurgeJob:Enabled` (off outside Development until LEG-01). Idempotent; safe to re-run.
- Opportunity import: fixture only; SAM.gov adapter pending P1.2.

## Incident response

- Suspected sensitive content: a company admin quarantines the document (`POST /api/v1/documents/{id}/quarantine`), which blocks access immediately. Then follow the incident procedure. **TBD (OPS-02):** security owner contact, evidence preservation, notification obligations.
- **TBD:** on-call rotation, alert destinations, escalation contacts.

## Deletion and retention

See [ADR-0003](../decisions/ADR-0003-configurable-retention.md). Do not state that data is "deleted everywhere" while backups or external systems may still hold copies.
