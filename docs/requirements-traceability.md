# Requirements traceability

Maps the handoff §14 matrix to what this repository contains today. "P0 delivered" means working code with tests.

| Requirement area | Source | Planned | P0 delivered | Evidence |
|---|---|---|---|---|
| Users, companies, roles | requirements.md §1 | P0/P1 | Identity sign-up/sign-in, MFA (TOTP) pages, company onboarding, company roles, platform-admin role with MFA policy | `ApiSecurityTests`, browser smoke test |
| Tenant isolation | handoff §5, SEC-01 | P0 | Service filters, fail-closed query filters, 404 on cross-tenant IDs, audited denials | `TenantIsolationTests`, `ApiSecurityTests` |
| SAM.gov monitoring | requirements.md §2; research | P1 | Source abstraction, idempotent resumable import, revisions, sync state; **fixture source only** (labeled) | `OpportunityImportTests` |
| Notice taxonomy (RFI/RFP/RFQ/Sources Sought; notice vs response type) | research; handoff §3.1 | P0 | Separate enums, raw value kept, versioned mapping rules, user override in domain | `ResponseTypeMapperTests`, `DomainTests` |
| Custom filters / saved searches | requirements.md §2 | P1 | NAICS, agency, keywords, notice/response type, set-aside, state, dates; tenant-owned saved searches | `OpportunityFilterTests`, smoke test |
| Alerts | requirements.md §2 | P1.6 | Frequency stored only | — |
| Fit scoring, Go/No-go | requirements.md §3 | P1.3 | Not started | — |
| Proposal lifecycle, compliance matrix | requirements.md §4–5; handoff §7 | P1.4 | Lifecycle state machine and compliance statuses in Domain | `ProposalLifecycleTests` |
| Documents | requirements.md §4; handoff §9B | P1.5 / P0-SEC | Secure upload, classification, marker rejection, private storage, authorized download, soft delete/restore, quarantine | `DocumentSecurityTests`, `ApiSecurityTests` |
| AI assistance | requirements.md §6 | P2/P3 opt-in | Provider abstraction, policy gate, disabled by default; no endpoint | `AiGovernanceTests` |
| Knowledge, analytics, integrations | requirements.md §7–9 | P2 | Not started | — |
| Security & compliance considerations | requirements.md §10 | P0+ | Classification, audit, CSRF, secure cookies, rate limits, no certification claims | `docs/production-gates.md` |
| Retention | handoff §9G.2 | P0 (configurable) | Configured, versioned policies; purge job; legal-hold flag | `RetentionTests` |
| Cloud hosting | handoff D-01 | P0 skeleton | Bicep skeleton for Container Apps, PostgreSQL, Blob, Key Vault, managed identity | CI Bicep build |
| Accessibility (Section 508 / WCAG 2.2 AA target) | requirements.md Technical §4 | P1.7 | Semantic forms, labels, skip link, live regions, focus styles; **no conformance claim** | — |
| Subscriptions/entitlements | requirements.md Business §1 | P1 | Not started; no payment provider | — |
