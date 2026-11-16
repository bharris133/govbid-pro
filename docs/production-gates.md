# Unresolved production gates

This list is controlling for release decisions. Engineering may build and test locally behind configuration (handoff §9H.1), but **nothing below is approved** until the named owner signs it off. Update this file in the same pull request that records an approval.

Legend: **Blocked** = no production use until approved. **Configured off** = code path exists but is disabled by default and guarded at startup.

## Owner decision gates (handoff §9H.1)

| Gate | Owner | What is still needed | Current engineering state |
|---|---|---|---|
| **OPS-01** | Product / engineering | Azure subscription and tenant, production region, budget ceiling, environment SKUs, alert destinations, ownership and support access | **Blocked.** `infra/main.bicep` has no default region; only `dev.bicepparam` exists and reads region and secrets from the environment. No production parameter file. CI compiles Bicep but never deploys. |
| **OPS-02** | Product / security | RTO, RPO, backup/restore objectives, incident escalation contact, external security testing, operational response owner | **Blocked.** A dump-and-restore drill (`scripts/restore-drill.sh`) runs in CI against a test database and the runbook describes restore and incident steps, but no drill has been run against a hosted provider's backups, and RTO/RPO and contacts are undecided. |
| **LEG-01** | Product / legal | Exact deletion SLA, service and privacy notices, personal-data rights, contractual exceptions, legal holds, subprocessors, cross-border access | **Blocked.** Retention periods are the provisional D-02 defaults in configuration and versioned in the database. Automated purge is **off** outside Development. Legal-hold field exists; the hold workflow is not built. |
| **AI-01** | Product / security / legal | Specific Azure AI service, model, deployment, region, commercial terms, usage restrictions, retention/abuse-monitoring settings, feature consent, acceptable prompt metadata | **Configured off.** `Ai:PlatformEnabled=false`, provider `None`, empty feature allowlist. Startup fails if AI is enabled without an `Ai01ApprovalReference`. `AzureAiProvider` deliberately throws. No AI endpoint is exposed. |
| **AI-02** | Product / customer | Whether CONFIDENTIAL_COMMERCIAL documents may ever go to an external AI provider, plus explicit customer opt-in | **Configured off.** `DataHandling:ConfidentialCommercialAiApproved=false`; the policy gate denies confidential inputs with `ai.confidential_requires_ai02_approval`. |
| **R2 / R3** | Compliance owner | FCI/CUI or agency processing boundary, contract clauses, assessment/authorization, appropriate Azure environment | **Blocked.** Upload flow rejects declared restricted/classified content and detected markings before storage. No R2/R3 feature flags exist. |

## Release 1 blocking acceptance criteria (handoff §9F.2)

| ID | Status in this P0 build | Evidence today | Remaining work |
|---|---|---|---|
| SEC-01 Cross-tenant denial | Partial | `TenantIsolationTests`, `ApiSecurityTests`, per-feature cross-tenant tests (proposals, documents, collaboration), `HardeningTests` (every tenant-owned entity must have a query filter; audit export limited to the caller's company) | External penetration test; caches and search indexes if they are added |
| SEC-02 Classification tests at every boundary | Partial | `ClassificationPolicyTests` (full matrix), upload and AI gate tests | Export, notification and search boundaries when implemented |
| SEC-03 Restricted content never reaches storage/AI | Partial | `DocumentSecurityTests`, browser smoke test | Detection is best-effort (compressed PDF text is not inspected); human escalation workflow |
| SEC-04 Admin MFA, invite/deprovision, audit, cookies/CSRF, rate limiting, secrets | Partial | MFA-required admin policy, CSRF tests, secure `__Host-` cookies, rate limiter, Key Vault references, invitations and member removal (`CollaborationTests`), company audit export for access reviews (`HardeningTests`) | A configured email provider for account confirmation, MFA for company admins, a scheduled access-review process |
| SEC-05 Clean scans, restore drill, incident runbook | Partial | CI: dependency audit, gitleaks, Trivy, CodeQL, dump-and-restore drill | Provider backup restore drill with evidence (OPS-02), incident runbook owner, pen-test plan |
| SEC-06 Retention/deletion/export verified | Partial | `RetentionTests` (configurable windows, idempotent purge, legal hold, versioning) | Account closure/export, search/index purge, backup expiry evidence (LEG-01) |
| SEC-07 AI off by default; AI-01..06 tests | Partial | `AiGovernanceTests` (off by default, no consent, cross-tenant, confidential, injection, outage) | AI-05 output validation; formal provider approval |
| SEC-08 No false claims; prohibited-data notice | Done for P0 pages | `/DataPolicy`, home page, footer | Onboarding emails, marketing and support material |

## Other known gaps before production

- **Live SAM.gov sync is built but not verified.** The adapter follows the official Get Opportunities Public API v2 documentation and is tested against a fake server, but it has never been run with a real API key. It is off by default and the app still labels fixture data as demo data. Complete the checklist in [operations/sam-gov-sync.md](operations/sam-gov-sync.md) before relying on it.
- **Malware scanning must be switched on per environment.** The default is `NotConfigured`: uploads are stored but downloads are denied. P1.5 adds a ClamAV adapter (`Documents:MalwareScanMode=ClamAv`, `Documents:ClamAv:Host`) and a rescan job that clears files stored before the scanner was available. Before production: run clamd on the private network only, keep signatures updating (freshclam), size it for the upload limit (clamd `StreamMaxLength` must be at least `Documents:MaxUploadBytes`), and test with the EICAR file. ClamAV has no licence cost; hosting it is the cost. The live adapter has only been tested against a protocol double, not a real clamd.
- **Email must be configured per environment.** The default is `Email:Provider=None`: nothing is sent, invitations show a link for the admin to pass on, and notifications stay in the app. Production requires confirmed accounts, so set `Email:Provider=Smtp` with `Email:Smtp:*`, `Email:FromAddress` and `Collaboration:PublicBaseUrl` (https), with the password from the secret store. The SMTP adapter has not been exercised against a real mail server yet; send a test invitation and a password reset before go-live, and set up SPF/DKIM for the sending domain.
- **PostgreSQL authentication.** The skeleton uses a password held in Key Vault. Switching to Microsoft Entra authentication with managed identity is recommended (ADR follow-up).
- **Observability export.** App Insights is provisioned; OpenTelemetry export from the app is not wired yet.
- **Tamper-evident audit storage.** Audit events are append-only in the application (no update or delete path) and company admins can export their own trail, but a database administrator could still alter rows. Before production, ship audit events to write-once storage or a log service the application cannot modify, and decide the retention period (LEG-01).
- **Deployment pipeline.** CI builds, tests, scans and compiles the infrastructure templates but deploys nothing, by decision. A release pipeline with a migration step and an approval gate is still to be built once OPS-01 is settled.
- **Performance.** Only a single-user baseline exists ([operations/performance-baseline.md](operations/performance-baseline.md)). No load test has been run, and keyword search needs a full-text index before large live data.
- **GitHub Actions** are pinned to major versions, not commit SHAs. Pin to SHAs before production use.
- **Payments**: no billing provider selected; none is integrated.
- **Accessibility**: WCAG 2.2 AA / Section 508 patterns are used and automated axe checks on the main pages pass in CI. Automated checks cannot establish conformance; a manual evaluation (keyboard, screen reader, zoom) has not been performed and no conformance is claimed.
