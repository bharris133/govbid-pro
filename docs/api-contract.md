# API contract (v1)

All endpoints are under `/api/v1`, return JSON (camelCase, enums as strings) and use RFC 9457 problem details with a `reasonCode` extension on errors. Authentication is the same-origin Identity cookie. Every unsafe request (POST, PUT, PATCH, DELETE) must send the antiforgery token in the `X-CSRF-TOKEN` header; the token is rendered in `<meta name="csrf-token">` by the Razor layout.

| Method | Route | Auth | Behavior |
|---|---|---|---|
| GET | `/health/live` | none | Liveness, no checks, no details. |
| GET | `/health/ready` | none | Database readiness, no details. |
| GET | `/api/v1/me` | signed in | User, resolved company and role, AI and SAM.gov status flags. |
| GET | `/api/v1/dashboard` | tenant member | Discovery counts (open, due in 7 days, new, amended), upcoming deadlines, the company's saved searches with open and new match counts, and source freshness. |
| GET | `/api/v1/opportunities` | tenant member | Filters: `keywords`, `naics`*, `agency`*, `noticeType`*, `responseType`*, `setAside`*, `state`*, `postedFrom`, `postedTo`, `dueAfter`, `dueBefore`, `openOnly`, `sort` (`PostedNewest`, `DueSoonest`), `page`, `pageSize` (max 100). Returns `results` (paged) and `sources` (freshness and live/fixture flag). *repeatable |
| GET | `/api/v1/opportunities/{id}` | tenant member | Detail with source URL, raw notice type, deadline text/time zone, submission instructions and revision history. |
| GET | `/api/v1/company/profile` | tenant member | The company's fit profile (NAICS, declared set-aside programs, keywords, states, agencies, minimum response days). |
| PUT | `/api/v1/company/profile` | TeamManager+ (configurable) | Replace the fit profile. 400 `company_profile.invalid`. |
| GET | `/api/v1/opportunities/{id}/fit` | tenant member | Explainable fit score (`fit-v1`): six weighted criteria, each with points and a reason, plus cautions. Nothing is saved. |
| GET | `/api/v1/opportunities/{id}/assessments` | tenant member | The company's assessment history for the notice, newest first. |
| POST | `/api/v1/opportunities/{id}/assessments` | ProposalWriter+ for `Watch`/`Undecided`; TeamManager+ (configurable) for `Go`/`NoGo` | Body `{ decision, rationale }`. Scores now and appends an immutable record (score, criteria snapshot, notice revision, author). Rationale required for Go/No-go. 201. |
| GET | `/api/v1/assessments?decision=` | tenant member | Latest assessment per opportunity, with `opportunityAmendedSince`. |
| GET | `/api/v1/company/members` | tenant member | Active members (id, email, role) for assignment and reviewer pickers. |
| GET | `/api/v1/proposals?includeClosed=` | tenant member | The company's proposals with status label, owner, due date and compliance summary. Closed (Won, Lost, Cancelled) hidden unless `includeClosed=true`. |
| POST | `/api/v1/proposals` | ProposalWriter+ | Body `{ opportunityId, title?, responseType? }`. Starts a response: snapshots the notice revision and deadline and adds default sections for the response type. 409 if the company already has an open proposal for the notice. 201. |
| GET | `/api/v1/proposals/{id}` | tenant member | Detail: overview, configured stage labels, `nextStages` with `allowed` and `blockedReason`, compliance summary, sections, requirements, reviews (with `currentRound`) and status history. |
| PUT | `/api/v1/proposals/{id}` | ProposalWriter+ | Body `{ title, responseType, ownerUserId }`. Owner must be an active member. 409 when closed. |
| POST | `/api/v1/proposals/{id}/status` | ProposalWriter+; TeamManager+ (configurable) for Approved and later stages | Body `{ to, note?, submissionEvidence? }`. 409 with a reason code when the transition is not allowed, review approval is missing, pending or sent back, or mandatory requirements are open. `SubmittedExternally` needs evidence; GovBid Pro never submits. Moving back to In progress supersedes pending reviews. |
| POST | `/api/v1/proposals/{id}/sections` | ProposalWriter+ | Body `{ title, assigneeUserId?, isComplete }`. |
| PUT | `/api/v1/proposals/{id}/sections/{sectionId}` | ProposalWriter+ | Rename, assign or complete a section. Assignee must be an active member. |
| GET | `/api/v1/proposals/{id}/requirements` | tenant member | Compliance matrix rows in natural reference order (L.2 before L.10). |
| POST | `/api/v1/proposals/{id}/requirements` | ProposalWriter+ | Body `{ reference, text, sourceLocator?, category?, mandatory, sectionId?, responseLocation?, assigneeUserId?, evidence?, status }`. References are unique per proposal (case-insensitive, 409). `Exception` status needs evidence. 201. |
| PUT | `/api/v1/proposals/{id}/requirements/{requirementId}` | ProposalWriter+ | Replace a row. |
| DELETE | `/api/v1/proposals/{id}/requirements/{requirementId}` | ProposalWriter+ | 204. |
| POST | `/api/v1/proposals/{id}/reviews` | ProposalWriter+ | Body `{ reviewerUserId, instructions? }`. Only while In review. The owner cannot review their own proposal unless configured (`review.self_review`). |
| POST | `/api/v1/proposals/{id}/reviews/{reviewId}/decision` | the assigned reviewer (Reviewer+) | Body `{ outcome: Approved \| ChangesRequested, comment? }`. Changes requested needs a comment. 403 `review.not_assigned_reviewer` for anyone else. |
| GET | `/api/v1/saved-searches` | tenant member | The company's saved searches. |
| GET | `/api/v1/saved-searches/{id}` | tenant member | 404 for other tenants' IDs. |
| POST | `/api/v1/saved-searches` | ProposalWriter+ | Body `{ name, criteria, notificationFrequency }`. 201. |
| PUT | `/api/v1/saved-searches/{id}` | ProposalWriter+ | Replace name, criteria and alert frequency. 404 across tenants. |
| DELETE | `/api/v1/saved-searches/{id}` | ProposalWriter+ | 204; 404 across tenants. |
| GET | `/api/v1/documents` | tenant member | The company's non-purged documents. |
| POST | `/api/v1/documents` | ProposalWriter+ | Multipart: `file`, `classification`, `affirmPermittedToUpload`, `affirmNoProhibitedContent`. 201, or 403 with an `upload.*` reason code. |
| GET | `/api/v1/documents/{id}/content` | tenant member | Authorized stream, `Cache-Control: no-store`. 403 if quarantined, deleted or not scanned clean; 404 across tenants. |
| DELETE | `/api/v1/documents/{id}` | ProposalWriter+ | Soft delete with configured recovery window. |
| POST | `/api/v1/documents/{id}/restore` | ProposalWriter+ | Restore within the recovery window. |
| POST | `/api/v1/documents/{id}/quarantine` | CompanyAdmin | Blocks all access. |
| GET | `/api/v1/admin/imports` | PlatformAdmin + MFA | Recent import runs. |
| GET | `/api/v1/admin/imports/sources` | PlatformAdmin + MFA | Per-source sync state: last success, cursor, backoff, last error code. |
| POST | `/api/v1/admin/imports/sync` | PlatformAdmin + MFA | Runs one bounded sync of every enabled live source. 409 when none is enabled. |
| POST | `/api/v1/admin/imports/fixture` | PlatformAdmin + MFA | Runs the labeled fixture import. |


## Status codes

401 unauthenticated API call (no redirect) · 403 policy/role denial or missing tenant · 404 not found or another tenant's resource · 400 validation or missing/invalid antiforgery token · 409 state conflict · 429 rate limited · 503 AI provider unavailable (when AI is enabled in future).
