# Requirements traceability

Maps the handoff §14 matrix to what this repository contains today. "P0 delivered" means working code with tests.

| Requirement area | Source | Planned | P0 delivered | Evidence |
|---|---|---|---|---|
| Users, companies, roles | requirements.md §1 | P0/P1 | Identity sign-up/sign-in, MFA (TOTP) pages, company onboarding, company roles, platform-admin role with MFA policy | `ApiSecurityTests`, browser smoke test |
| Tenant isolation | handoff §5, SEC-01 | P0 | Service filters, fail-closed query filters, 404 on cross-tenant IDs, audited denials | `TenantIsolationTests`, `ApiSecurityTests` |
| SAM.gov monitoring | requirements.md §2; research | P1 | Source abstraction, idempotent resumable import, revisions, sync state; **fixture source only** (labeled) | `OpportunityImportTests` |
| Notice taxonomy (RFI/RFP/RFQ/Sources Sought; notice vs response type) | research; handoff §3.1 | P0 | Separate enums, raw value kept, versioned mapping rules, user override in domain | `ResponseTypeMapperTests`, `DomainTests` |
| Custom filters / saved searches | requirements.md §2 | P1 | NAICS, agency, keywords, notice/response type, set-aside, state, dates; tenant-owned saved searches | `OpportunityFilterTests`, smoke test |
| Opportunity discovery dashboard | requirements.md §2; handoff P1.1 | P1.1 | Dashboard (open, due soon, new, amended, upcoming deadlines, saved-search match counts), detail page with provenance, paging, sorting, more filters | `DashboardTests`, `DiscoveryApiTests`, smoke test |
| Live SAM.gov sync | handoff §6, P1.2 | P1.2 | Official v2 API adapter, bounded scheduled sync, admin endpoints; off by default and **not verified live** | `SamGovSourceTests`, `OpportunityImportTests` |
| Fit scoring, Go/No-go | requirements.md §3 | P1.3 | Company fit profile, explainable rule-based score, role-controlled Go/No-go/Watch with rationale, append-only history, amended-since flag, dashboard counts | `FitScorerTests`, `AssessmentTests`, `AssessmentApiTests`, smoke test |
| Proposal lifecycle, compliance matrix | requirements.md §4–5; handoff §7 | P1.4 | Start a response from an opportunity, default sections by response type, compliance matrix with owners and evidence, assigned reviews with four-eyes default, gated lifecycle (review approval, mandatory items resolved, submission evidence), status history, configurable stage names | `ProposalDomainTests`, `ProposalLifecycleTests`, `ProposalTests`, `ProposalApiTests`, smoke test |
| Documents | requirements.md §4; handoff §9B | P1.5 / P0-SEC | Secure upload, classification, marker rejection, private storage, authorized download, soft delete/restore, quarantine; versions, proposal links, templates and copy, ClamAV and Azure Defender for Storage scanner adapters with rescan job, purge of every version, compliance matrix CSV and outline exports | `DocumentSecurityTests`, `ApiSecurityTests`, `DocumentVersionTests`, `DocumentApiTests`, `MalwareScannerTests`, smoke test |
| Collaboration and alerts | requirements.md §5–6; handoff §8 | P1.6 | Invitations (single use, expiring, email-bound), role changes and removal with a last-admin guard, in-app notifications with optional email and opt-out, tasks, proposal comments with moderation, Daily/Weekly saved-search alerts without repeats, provider-neutral email port | `CollaborationDomainTests`, `CollaborationTests`, `CollaborationApiTests`, smoke test |
| AI assistance | requirements.md §6 | P2/P3 opt-in | Provider abstraction, policy gate, disabled by default; no endpoint | `AiGovernanceTests` |
| Knowledge, analytics, integrations | requirements.md §7–9 | P2 | Not started | — |
| Security & compliance considerations | requirements.md §10 | P0+ | Classification, audit, CSRF, secure cookies, rate limits, no certification claims | `docs/production-gates.md` |
| Retention | handoff §9G.2 | P0 (configurable) | Configured, versioned policies; purge job; legal-hold flag | `RetentionTests` |
| Cloud hosting | handoff D-01 | P0 skeleton | Bicep skeleton for Container Apps, PostgreSQL, Blob, Key Vault, managed identity | CI Bicep build |
| Accessibility (Section 508 / WCAG 2.2 AA target) | requirements.md Technical §4 | P1.7 | Semantic forms, labels, skip link, live regions, focus styles. Automated axe checks (WCAG A/AA rules) on the main pages run in CI. Automated checks find only part of the issues; **no conformance claim** until a manual evaluation with assistive technology | `accessibility.spec.ts` |
| Hardening | handoff P1.7 | P1.7 | Structural tenant-filter guardrail, company audit export, restore drill script run in CI, performance baseline, runbook. Deployment pipeline, provider restore drill, external security test and tamper-evident audit storage remain gated | `HardeningTests`, `scripts/restore-drill.sh`, `docs/operations/performance-baseline.md` |
| Subscriptions/entitlements | requirements.md Business §1 | P1 | Not started; no payment provider | — |
