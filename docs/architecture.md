# Architecture

GovBid Pro is a modular monolith (see [ADR-0001](decisions/ADR-0001-modular-monolith-stack.md)) hosted on Azure ([ADR-0002](decisions/ADR-0002-azure-hosting.md)).

```
Browser (Razor Pages + compiled TypeScript ES modules)
   │  same-origin HttpOnly cookie + X-CSRF-TOKEN on writes
   ▼
GovBidPro.Web ── controllers (/api/v1), Razor Pages, Identity UI, middleware:
   │             security headers → rate limiter → authentication → tenant resolution → authorization
   ▼
GovBidPro.Application ── services: opportunities (query/import), saved searches, documents,
   │                      data policy, retention, AI gate/task, companies; ports (IAppDbContext,
   │                      IObjectStorage, IMalwareScanner, IOpportunitySource, IAiProvider, IAuditLog)
   ▼
GovBidPro.Domain ── entities, enums, invariants (no dependencies)

GovBidPro.Infrastructure ── EF Core/Npgsql (+ fail-closed tenant query filters), Identity store,
                            Local/Azure Blob storage, scanners, fixture source, AI providers, DI
```

## Trust boundaries

See [security/threat-model.md](security/threat-model.md). Untrusted inputs: browser requests, uploaded files, external notice sources, and (future) AI provider responses.

## Data placement

| Data | Classification | Where |
|---|---|---|
| Opportunities, revisions, sync state, import runs | Public | PostgreSQL, global tables |
| Companies, memberships, saved searches | BusinessInternal | PostgreSQL, tenant-scoped |
| Uploaded documents | BusinessInternal / ConfidentialCommercial | Private object storage; metadata in PostgreSQL |
| Audit events, AI request metadata, retention records | Operational, content-free | PostgreSQL |
| Data Protection keys | Secret | Blob (Azure) wrapped by Key Vault key |

## Request flow example: document download

1. Cookie authentication, then `TenantResolutionMiddleware` resolves the active membership server-side.
2. `TenantMember` policy requires a resolved tenant.
3. `DocumentService.OpenDownloadAsync` asks `IDataPolicyEvaluator` (tenant, state, classification, scan).
4. Denials are audited and returned as 404 (cross-tenant) or 403 (policy); success streams from private storage with `no-store`.

## What is intentionally not here yet

Live SAM.gov adapter (P1.2), worker process, notifications, assessments, proposals API, compliance matrix, exports, AI endpoints, billing. See the ordered plan in the handoff §12 and [production-gates.md](production-gates.md).
