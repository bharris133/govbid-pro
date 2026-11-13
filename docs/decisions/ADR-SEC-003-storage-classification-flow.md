# ADR-SEC-003: Storage and classification flow

- Status: Accepted
- Date: 2026-11-13
- Source: Handoff v4 §9B.2, §9B.3

## Upload pipeline (in order; every rejection happens before storage and is audited without content)

1. Role check (ProposalWriter or higher) and resolved tenant.
2. Declaration: classification must be BusinessInternal or ConfidentialCommercial; both affirmations required.
3. Declared size and tenant quota.
4. Bounded read (hard cap regardless of the declared length).
5. Type allowlist: extension + declared content type + file signature (PDF, DOCX, XLSX, TXT, CSV, PNG, JPG). DOCX/XLSX must be valid OOXML zips.
6. Marking detection (classified banners, CUI banners/designators, FOUO). Best-effort; compressed PDF streams are not inspected in P0.
7. Malware scan through `IMalwareScanner`. Infected files are rejected. With no scanner configured the object is stored as `ScannerNotConfigured` and downloads stay denied.
8. Store in private object storage at `tenants/{tenantId}/documents/{documentId}/v1`, then persist metadata: tenant, classification, declared by/at, source, retention policy, object version, scan status, SHA-256, correlation id.

## Access

- `IDataPolicyEvaluator` is the authoritative check for every document access: tenant match, state (quarantined/deleted/purged denied), classification by purpose, and a clean scan for read/export/search/AI.
- Downloads are streamed through the API with `Cache-Control: no-store`. No public URLs, SAS tokens or wwwroot paths.
- Logs, audit events and problem details never include document bodies.

## Storage implementations

- `LocalFileObjectStorage`: Development/Testing only; path outside wwwroot; key validation prevents traversal.
- `AzureBlobObjectStorage`: managed identity, private container, `If-None-Match: *` to avoid overwrites. Startup rejects Local storage outside Development/Testing.
