# ADR-SEC-002: Tenant identity and authorization

- Status: Accepted
- Date: 2026-11-13
- Source: Handoff v4 §5, §9, §9D

## Decision

- **Tenant = Company.** Tenant-owned entities implement `ITenantOwned`. Opportunities are global public data and are not tenant-owned.
- **Server-side tenant resolution.** `TenantResolutionMiddleware` reads the authenticated user's active membership from the database on each request and populates a request-scoped `TenantContextHolder`. Client input never selects the tenant. The holder can be set once.
- **Defense in depth.**
  1. Services call `RequireTenant()` / `RequireRole()` and filter every query by `TenantId` explicitly.
  2. EF Core global query filters on tenant-owned sets **fail closed**: with no tenant (and no explicit system context) they return no rows.
  3. Cross-tenant identifiers return 404 (not 403) so tenants cannot probe for each other's IDs, and the denial is audited.
- **Roles.** Company roles: Reviewer < ProposalWriter < TeamManager < CompanyAdmin. Platform administration is a separate Identity role (`PlatformAdmin`) that grants no tenant access and requires an MFA sign-in (`amr=mfa`) via the `PlatformAdminWithMfa` policy.
- **Sessions.** ASP.NET Core Identity cookies: HttpOnly, Secure, SameSite=Strict, `__Host-` prefix outside local development. Antiforgery tokens are required on every unsafe API request (`X-CSRF-TOKEN` header) and on every form. No tokens in browser storage.
- **Throttling.** Identity lockout (5 attempts, 15 minutes) plus a partitioned rate limiter with a tighter limit for login posts.
- **Background jobs** use an explicit system context (`SetSystem()`); purge refuses to run without it.

## Consequences

- Each API request performs one membership lookup; caching can come later with care for revocation.
- P0 supports one membership per user. Multi-company switching is an open product question.
- Invitations, deprovisioning UI, and account email confirmation are not built yet (SEC-04 remaining work).
