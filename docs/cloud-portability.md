# Cloud portability

Owner rule: GovBid Pro runs on Azure today ([ADR-0002](decisions/ADR-0002-azure-hosting.md)) but must be able to move to another
provider for security, reliability or cost reasons without rewriting features
([ADR-0004](decisions/ADR-0004-cloud-portability.md)).

## How it works

1. **The app is a standard container.** One `Dockerfile`, no cloud agent or SDK needed to start it. It runs the same way under
   Docker Compose, Azure Container Apps, AWS ECS/Fargate, Google Cloud Run, Kubernetes or a single VM.
2. **The database is standard PostgreSQL 16.** No provider extensions are used. Every provider offers managed PostgreSQL, and
   `pg_dump`/`pg_restore` moves the data (see the [runbook](operations/runbook.md)).
3. **Everything provider-specific sits behind a port** (an interface in `GovBidPro.Application`). Adapters live only in
   `GovBidPro.Infrastructure`, and a configuration value picks the adapter. Domain, Application and Web never see a cloud SDK type.
4. **A test enforces the boundary.** `CloudPortabilityTests` fails the build if a cloud SDK namespace or package appears outside
   `GovBidPro.Infrastructure`.

## Swap points

| Concern | Port (Application) | Setting | Adapters today | To move provider |
|---|---|---|---|---|
| File storage | `IObjectStorage` | `Storage:Provider` | `Local` (dev), `FileSystem` (any host with a persistent volume), `AzureBlob` | Add one adapter class (about 70 lines, three methods) and one line in `StorageProviders`/`DependencyInjection`. An S3-compatible adapter covers AWS, Cloudflare R2, Backblaze, DigitalOcean Spaces and MinIO. |
| Sign-in key storage | ASP.NET Core Data Protection | `Storage:DataProtectionKeyVaultKeyUri` or `Storage:DataProtectionKeyDirectory` | Azure Blob + Key Vault, or a private directory | Point at the new provider's key store, or use the directory option on a private volume. |
| Secrets | .NET configuration | environment variables | Azure Key Vault references resolved by the host | Use the new host's secret store to inject the same environment variables. No code reads a vault directly. |
| Malware scanning | `IMalwareScanner`, `IStoredObjectMalwareScanner` (scan-after-store platforms) | `Documents:MalwareScanMode`, `Documents:ClamAv` | `NotConfigured`, `ClamAv` (clamd over TCP; open source, runs as a container anywhere), `AzureDefender` (Defender for Storage on-upload scanning, verdict read from a blob index tag; [ADR-0005](decisions/ADR-0005-azure-defender-malware-scanning.md)), `DevelopmentBypass` (dev) | Set `ClamAv`, or add one `IStoredObjectMalwareScanner` adapter for the new provider's bucket scanner (for example GuardDuty Malware Protection for S3). Files without a verdict stay `Pending`; `DocumentRescanService` catches up. |
| Email | `IEmailSender` | `Email:Provider`, `Email:Smtp`, `Collaboration:PublicBaseUrl` | `None` (default), `Smtp` (any provider that offers SMTP), `Log` (dev) | Change the SMTP host and credentials. A provider's HTTP API would be one more adapter. Account emails from the sign-in system go through the same port. |
| Opportunity sources | `IOpportunitySource` | `OpportunitySources:*` | Fixture | Not cloud-specific. |
| AI (off) | `IAiProvider` | `Ai:Provider` | `None`, `Azure` (skeleton) | Add an adapter; the policy gate is provider-neutral. |
| Background jobs | .NET hosted services | per-job `Enabled`/interval settings | In-process timers (retention purge, opportunity sync, document rescan, saved-search alerts) | Nothing to change. No cloud queue or scheduler is used. |
| Infrastructure as code | — | `infra/` | Azure Bicep | Rewrite for the new provider (Terraform or its native tool). This is the only Azure-only directory. |

## Moving to another provider: checklist

1. Provision a container host, managed PostgreSQL 16, private object storage and a secret store on the new provider.
2. Write the storage adapter if the provider is not already covered, and set `Storage:Provider`. If the environment used
   `AzureDefender` scanning, switch `Documents:MalwareScanMode` to `ClamAv` or the new provider's scanner adapter.
3. Set the same environment variables the app reads today (`ConnectionStrings__GovBidPro`, `Storage__*`, and so on).
4. Stop writes, `pg_dump` the database and restore it on the new server. Copy the document objects with the same keys
   (`tenants/{tenant}/documents/{id}/v{n}`); storage keys are provider-neutral.
5. Move Data Protection keys, or accept that all users sign in again.
6. Point DNS at the new host. Keep the old environment until the restore has been verified.

## What is not portable

- `infra/*.bicep` (Azure only, by design).
- Azure-specific compliance scope. A FedRAMP or Azure Government boundary would have to be re-established on the new provider.
