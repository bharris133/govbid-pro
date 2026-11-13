# ADR-0003: Configurable, versioned retention policies

- Status: Accepted (owner decision D-02: provisional defaults). Final values pending LEG-01.
- Date: 2026-11-13
- Source: Handoff v4 §9G.2, RET-01..04

## Decision

- Retention periods live only in configuration (`Retention:Policies`), never in code. Startup fails if any required policy key is missing (`RetentionPolicyKeys.Validate`).
- At startup, `RetentionPolicyCatalog` writes each configured policy to `retention_policies` as a versioned row with the approval status text. A changed value creates a new version and supersedes the old one.
- Every stored document references the policy that governs it. Deletions create `retention_actions` rows (policy id and version, recoverable-until, legal hold, status, actor, correlation id). Dates are UTC.
- `RetentionPurgeService` is idempotent and resumable, runs only in an explicit system context, skips legal holds, records failures for retry, and audits each purge.
- The purge job is enabled in Development and disabled elsewhere until LEG-01.

Provisional values (D-02): active documents while the account is active; deleted-document recovery 30 days; closed-account export 30 days; database backups 30 days; security audit events 12 months (365 days); AI request metadata 90 days.

## Consequences

- Purge removes live objects and database state only. Objects can remain in still-retained backups until normal expiry (RET-03), so the product never claims "deleted everywhere".
- Closed-account export, search-index purge and backup-expiry verification are future work and are tracked in `docs/production-gates.md`.
