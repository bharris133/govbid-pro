# Unresolved production gates

This list is controlling for release decisions. Engineering may build and test locally behind configuration (handoff §9H.1), but **nothing below is approved** until the named owner signs it off. Update this file in the same pull request that records an approval.

Legend: **Blocked** = no production use until approved. **Configured off** = code path exists but is disabled by default and guarded at startup.

## Owner decision gates (handoff §9H.1)

| Gate | Owner | What is still needed | Current engineering state |
|---|---|---|---|
| **OPS-01** | Product / engineering | Azure subscription and tenant, production region, budget ceiling, environment SKUs, alert destinations, ownership and support access | **Blocked.** `infra/main.bicep` has no default region; only `dev.bicepparam` exists and reads region and secrets from the environment. No production parameter file. CI compiles Bicep but never deploys. |
| **OPS-02** | Product / security | RTO, RPO, backup/restore objectives, incident escalation contact, external security testing, operational response owner | **Blocked.** No restore drill performed. Runbook has placeholders only. |
| **LEG-01** | Product / legal | Exact deletion SLA, service and privacy notices, personal-data rights, contractual exceptions, legal holds, subprocessors, cross-border access | **Blocked.** Retention periods are the provisional D-02 defaults in configuration and versioned in the database. Automated purge is **off** outside Development. Legal-hold field exists; the hold workflow is not built. |
| **AI-01** | Product / security / legal | Specific Azure AI service, model, deployment, region, commercial terms, usage restrictions, retention/abuse-monitoring settings, feature consent, acceptable prompt metadata | **Configured off.** `Ai:PlatformEnabled=false`, provider `None`, empty feature allowlist. Startup fails if AI is enabled without an `Ai01ApprovalReference`. `AzureAiProvider` deliberately throws. No AI endpoint is exposed. |
| **AI-02** | Product / customer | Whether CONFIDENTIAL_COMMERCIAL documents may ever go to an external AI provider, plus explicit customer opt-in | **Configured off.** `DataHandling:ConfidentialCommercialAiApproved=false`; the policy gate denies confidential inputs with `ai.confidential_requires_ai02_approval`. |
| **R2 / R3** | Compliance owner | FCI/CUI or agency processing boundary, contract clauses, assessment/authorization, appropriate Azure environment | **Blocked.** Upload flow rejects declared restricted/classified content and detected markings before storage. No R2/R3 feature flags exist. |

## Release 1 blocking acceptance criteria (handoff §9F.2)

| ID | Status in this P0 build | Evidence today | Remaining work |
|---|---|---|---|
| SEC-01 Cross-tenant denial | Partial | `TenantIsolationTests`, `ApiSecurityTests` (saved searches, documents, policy evaluator, fail-closed query filter) | Search indexes, exports, background jobs and caches as they are built |
| SEC-02 Classification tests at every boundary | Partial | `ClassificationPolicyTests` (full matrix), upload and AI gate tests | Export, notification and search boundaries when implemented |
| SEC-03 Restricted content never reaches storage/AI | Partial | `DocumentSecurityTests`, browser smoke test | Detection is best-effort (compressed PDF text is not inspected); human escalation workflow |
| SEC-04 Admin MFA, invite/deprovision, audit, cookies/CSRF, rate limiting, secrets | Partial | MFA-required admin policy, CSRF tests, secure `__Host-` cookies, rate limiter, Key Vault references | Invitations and deprovisioning UI/flows, email sender for account confirmation, access reviews |
| SEC-05 Clean scans, restore drill, incident runbook | Partial | CI: dependency audit, gitleaks, Trivy, CodeQL | Restore drill (OPS-02), incident runbook owner, pen-test plan |
| SEC-06 Retention/deletion/export verified | Partial | `RetentionTests` (configurable windows, idempotent purge, legal hold, versioning) | Account closure/export, search/index purge, backup expiry evidence (LEG-01) |
| SEC-07 AI off by default; AI-01..06 tests | Partial | `AiGovernanceTests` (off by default, no consent, cross-tenant, confidential, injection, outage) | AI-05 output validation; formal provider approval |
| SEC-08 No false claims; prohibited-data notice | Done for P0 pages | `/DataPolicy`, home page, footer | Onboarding emails, marketing and support material |

## Other known gaps before production

- **Live SAM.gov integration is not built.** Official API documentation, access/keys, quotas and permitted use must be verified first (P1.2). The app runs in fixture-only mode and labels that data as demo data.
- **Malware scanning is not configured.** Uploads are stored but downloads are denied until a scanner is selected (candidate: Microsoft Defender for Storage malware scanning; needs an ADR and cost approval).
- **Account email.** No `IEmailSender` is configured; production requires confirmed accounts, so an email provider decision is needed.
- **PostgreSQL authentication.** The skeleton uses a password held in Key Vault. Switching to Microsoft Entra authentication with managed identity is recommended (ADR follow-up).
- **Observability export.** App Insights is provisioned; OpenTelemetry export from the app is not wired yet.
- **Tamper-evident audit storage** and the 12-month audit retention export (P1.7).
- **GitHub Actions** are pinned to major versions, not commit SHAs. Pin to SHAs before production use.
- **Payments**: no billing provider selected; none is integrated.
- **Accessibility**: WCAG 2.2 AA / Section 508 patterns are used, but no conformance evaluation has been performed and none is claimed.
