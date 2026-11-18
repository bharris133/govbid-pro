# Operations runbook

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

- PostgreSQL automated backups: retention parameterized (provisional 30 days). **TBD (OPS-02):** RTO/RPO, geo-redundancy, drill schedule.
- Document files live in object storage, not in the database. A complete restore needs both, taken close together. Turn on soft delete or versioning in the storage service, aligned to the deleted-document recovery window.

### Restore drill

`scripts/restore-drill.sh` backs up a database with `pg_dump`, restores it into a scratch database, and checks that the migration history and the row count of every table match. It only reads the source. It uses standard PostgreSQL tools, so it works the same on any host or cloud.

```bash
export PGHOST=<server> PGUSER=<role with CREATEDB> PGPASSWORD=<from the secret store, never typed into a file>
SOURCE_DB=govbidpro bash scripts/restore-drill.sh
```

- CI runs this drill on every pull request against the smoke-test database, so a schema change that cannot be dumped and restored fails the build.
- Before production (OPS-02): also restore the hosting provider's own backup (point-in-time restore) into a new server, run the application's `/health/ready` against it, and keep the evidence: date, who ran it, backup age (RPO achieved), time to restore (RTO achieved). Repeat at the agreed interval.
- After any restore to production, in this order: (1) apply pending migrations; (2) run the retention purge so documents deleted after the backup was taken are deleted again (RET-03); (3) check that every `document_versions.StorageKey` still exists in object storage; (4) expect users to redo work done after the backup time and tell them the cut-off.
- A restore brings back removed members and revoked invitations as they were at backup time. Review the member list and pending invitations with each affected company admin, or re-apply removals from the audit export.

## Scheduled jobs

All jobs run inside the web application on a timer, are safe to re-run, and do nothing when disabled. With more than one application instance, each instance runs them; they are idempotent, but move them to a single worker before scaling out (see cloud-portability.md).

| Job | Setting | Default | Notes |
|---|---|---|---|
| Retention purge | `Retention:PurgeJob:Enabled` | Off outside Development until LEG-01 | Deletes every stored version of a purged document. |
| Opportunity sync | `OpportunitySources:Sync:Enabled` | Off | Bounded request budget, resumes from a saved cursor. See [sam-gov-sync.md](sam-gov-sync.md). Fixture import runs at startup in Development only. |
| Document rescan | `Documents:Rescan:Enabled`, `IntervalMinutes` | On, every 15 minutes; does nothing without a scanner | Scans files stored while the scanner was unavailable. Files stay undownloadable until clean. |
| Saved-search alerts | `Collaboration:Alerts:Enabled`, `IntervalMinutes` | On, hourly check | Sends each opportunity once per saved search. In-app always; email only when email is configured and the member has not opted out. |

## Email

- `Email:Provider`: `None` (default, nothing sent), `Smtp` (any SMTP provider), `Log` (Development and Testing only; the recipient is masked in the log).
- SMTP needs `Email:Smtp:Host`, `Port`, `UserName`, `Email:FromAddress`, an https `Collaboration:PublicBaseUrl` (Development may use `http://localhost`), and the password from the secret store as `Email__Smtp__Password`.
- SendGrid: host `smtp.sendgrid.net`, port 587, user name `apikey`, the API key as the password, and a `FromAddress` verified in SendGrid. Locally these come from `.env`; see docs/LOCAL_SETUP.md section 7a.
- Check after a change: send yourself an invitation and a password reset. If email fails, invitations still work: the admin sees the link once and can pass it on.

## Malware scanner

- `Documents:MalwareScanMode`: `NotConfigured` (default; uploads stored, downloads denied), `ClamAv`, `DevelopmentBypass` (Development only).
- If clamd is unreachable, uploads are accepted as pending and cannot be downloaded; the rescan job clears them when the scanner returns. Users see "scan pending", not an error.
- An infected result blocks the version permanently and is audited. Do not move the file out of storage by hand; follow the incident steps below.

## Members and invitations

- Invitations expire after `Collaboration:InvitationExpiryDays` (7), work once, and only for the invited email address. Only a hash of the token is stored, so a lost link cannot be recovered: revoke and invite again.
- Removing a member ends access immediately on their next request and supersedes reviews waiting on them. A company always keeps one company admin.
- Access review: a company admin downloads the audit log from the Company page (`GET /api/v1/company/audit/export.csv?from=&to=`, at most 366 days per file). Each download is itself audited.

## Incident response

- Suspected sensitive content: a company admin quarantines the document (`POST /api/v1/documents/{id}/quarantine`), which blocks access immediately. Then follow the incident procedure. **TBD (OPS-02):** security owner contact, evidence preservation, notification obligations.
- Suspected account compromise: remove the member (Company page), revoke their pending invitations, have them reset their password, then export the audit log for the period and review what the account did.
- Suspected cross-company exposure: treat as a security incident. Capture the correlation id from the error or log line, export the audit logs of the companies involved, and do not delete data until the security owner has reviewed it.
- Leaked secret (database password, SMTP password, SAM.gov key): rotate it in the secret store, restart the application, and check the provider's own access logs. Secrets are never in the repository; CI scans for them.
- **TBD:** on-call rotation, alert destinations, escalation contacts.

## Deletion and retention

See [ADR-0003](../decisions/ADR-0003-configurable-retention.md). Do not state that data is "deleted everywhere" while backups or external systems may still hold copies.
