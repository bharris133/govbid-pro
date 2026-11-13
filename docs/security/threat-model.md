# Threat model (P0)

Scope: the P0 build in this repository. Revisit at each milestone (handoff §9D). Mitigations marked *planned* are tracked in [production-gates.md](../production-gates.md).

## Trust boundaries

1. **Browser ↔ Web**: untrusted users, including authenticated users of other tenants.
2. **Uploads ↔ Storage**: untrusted file content.
3. **External sources ↔ Ingestion**: SAM.gov and other platforms are untrusted (fixture-only today).
4. **Web ↔ AI provider**: future egress of selected data (off).
5. **Web ↔ Azure services**: PostgreSQL, Blob, Key Vault via managed identity.
6. **Operators ↔ Admin functions**: platform administrators.

## Threats and mitigations

| # | Threat | Mitigation in P0 | Gaps / planned |
|---|---|---|---|
| T1 | Cross-tenant data access by ID guessing (IDOR) | Server-side tenant resolution; explicit tenant filters; fail-closed EF query filters; 404 on mismatch; audited denials; tests | Search/index/export/job coverage as they are built |
| T2 | CSRF on cookie-authenticated mutations | Antiforgery header on all unsafe API requests; SameSite=Strict cookies; tests for missing/forged tokens | — |
| T3 | Session theft | HttpOnly, Secure, `__Host-` cookies; no tokens in JS storage; strict CSP without inline script | Session revocation on deprovision (planned) |
| T4 | Credential stuffing / brute force | Identity lockout; rate limiting on login and API | Bot protection, breached-password checks (planned) |
| T5 | Admin account compromise | Separate PlatformAdmin role, MFA required by policy, no tenant data access | Admin access reviews, just-in-time elevation (planned) |
| T6 | Upload of CUI/FCI/classified content | Declarations; marker detection rejects before storage; Restricted/Prohibited cannot be persisted; quarantine | Detection is best-effort; compressed PDFs not inspected; human escalation workflow (planned) |
| T7 | Malicious files (malware, polyglots, stored XSS) | Type allowlist with signature checks; downloads as attachments with no-store; no HTML/SVG; downloads denied until scanned | Malware scanner selection (planned) |
| T8 | Path traversal in storage | Generated storage keys; key regex and root check in local storage | — |
| T9 | Sensitive data in logs | Audit and logs carry IDs and reason codes only; problem details are generic | Log redaction tests (planned) |
| T10 | Fixture data mistaken for live government data | `isLiveSource=false`, `[FIXTURE]` titles, `example.invalid` URLs, UI banners | — |
| T11 | Malicious or malformed source data | Field length limits, required fields, revisions (no silent overwrite), backoff on errors | Live adapter hardening, outbound allowlist (P1.2) |
| T12 | Prompt injection / data leakage via AI | AI off; gate before retrieval; delimited untrusted data; no tools; human review; content-free metadata | Output validation (AI-05), provider approval (AI-01) |
| T13 | Secrets exposure | Key Vault + managed identity; no secrets in repo/images; gitleaks in CI | Rotate PostgreSQL password or move to Entra auth (planned) |
| T14 | Supply chain | Lock files, central package versions, digest-pinned base images, dependency audits, Trivy, SBOM, CodeQL | Pin GitHub Actions to SHAs; image signing (planned) |
| T15 | Data retained longer than disclosed | Configured, versioned retention; idempotent purge; legal hold | LEG-01 sign-off; backup expiry evidence |
| T16 | Denial of service | Request size limits, rate limiting, bounded queries (page size ≤ 100) | Azure-level protections and alerting (OPS-01) |
