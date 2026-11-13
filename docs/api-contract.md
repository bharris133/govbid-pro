# API contract (v1)

All endpoints are under `/api/v1`, return JSON (camelCase, enums as strings) and use RFC 9457 problem details with a `reasonCode` extension on errors. Authentication is the same-origin Identity cookie. Every unsafe request (POST, PUT, PATCH, DELETE) must send the antiforgery token in the `X-CSRF-TOKEN` header; the token is rendered in `<meta name="csrf-token">` by the Razor layout.

| Method | Route | Auth | Behavior |
|---|---|---|---|
| GET | `/health/live` | none | Liveness, no checks, no details. |
| GET | `/health/ready` | none | Database readiness, no details. |
| GET | `/api/v1/me` | signed in | User, resolved company and role, AI and SAM.gov status flags. |
| GET | `/api/v1/opportunities` | tenant member | Filters: `keywords`, `naics`*, `agency`*, `noticeType`*, `responseType`*, `setAside`*, `state`*, `postedFrom`, `postedTo`, `dueAfter`, `page`, `pageSize` (max 100). Returns `results` (paged) and `sources` (freshness and live/fixture flag). *repeatable |
| GET | `/api/v1/opportunities/{id}` | tenant member | Detail with source URL, raw notice type, deadline text/time zone, submission instructions and revision history. |
| GET | `/api/v1/saved-searches` | tenant member | The company's saved searches. |
| GET | `/api/v1/saved-searches/{id}` | tenant member | 404 for other tenants' IDs. |
| POST | `/api/v1/saved-searches` | ProposalWriter+ | Body `{ name, criteria, notificationFrequency }`. 201. |
| DELETE | `/api/v1/saved-searches/{id}` | ProposalWriter+ | 204; 404 across tenants. |
| GET | `/api/v1/documents` | tenant member | The company's non-purged documents. |
| POST | `/api/v1/documents` | ProposalWriter+ | Multipart: `file`, `classification`, `affirmPermittedToUpload`, `affirmNoProhibitedContent`. 201, or 403 with an `upload.*` reason code. |
| GET | `/api/v1/documents/{id}/content` | tenant member | Authorized stream, `Cache-Control: no-store`. 403 if quarantined, deleted or not scanned clean; 404 across tenants. |
| DELETE | `/api/v1/documents/{id}` | ProposalWriter+ | Soft delete with configured recovery window. |
| POST | `/api/v1/documents/{id}/restore` | ProposalWriter+ | Restore within the recovery window. |
| POST | `/api/v1/documents/{id}/quarantine` | CompanyAdmin | Blocks all access. |
| GET | `/api/v1/admin/imports` | PlatformAdmin + MFA | Recent import runs. |
| POST | `/api/v1/admin/imports/fixture` | PlatformAdmin + MFA | Runs the labeled fixture import. |

Planned (handoff §13, later milestones): `POST /opportunities/{id}/assessments`, `GET/POST /proposals`, `GET/POST /proposals/{id}/requirements`, `POST /proposals/{id}/reviews`.

## Status codes

401 unauthenticated API call (no redirect) · 403 policy/role denial or missing tenant · 404 not found or another tenant's resource · 400 validation or missing/invalid antiforgery token · 409 state conflict · 429 rate limited · 503 AI provider unavailable (when AI is enabled in future).
