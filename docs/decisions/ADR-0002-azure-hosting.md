# ADR-0002: Azure hosting

- Status: Accepted (owner decision D-01). Production configuration pending gate OPS-01.
- Date: 2026-11-13
- Source: Handoff v4 decision register D-01, §9G.1 CLOUD-01..04, §11

## Decision

Release 1 targets Microsoft Azure commercial cloud using the proposed pattern (CLOUD-02):

| Concern | Service | Notes |
|---|---|---|
| Compute | Azure Container Apps (workload profiles, VNet-integrated) | Same image as local Docker Compose. HTTPS-only ingress. |
| Database | Azure Database for PostgreSQL Flexible Server 16 | Private access via delegated subnet; `require_secure_transport=on`; backup retention parameterized (D-02 target 30 days). |
| Files | Azure Blob Storage | No public access, shared keys disabled, VNet rule only, soft delete aligned to the recovery window. |
| Secrets/keys | Azure Key Vault (RBAC) | Holds the DB connection string and the Data Protection wrapping key; purge protection on. |
| Identity | User-assigned managed identity | Blob Data Contributor, Key Vault Secrets User, Key Vault Crypto User (key-scoped). |
| Observability | Log Analytics + Application Insights | App-side OpenTelemetry export is a follow-up. |

Infrastructure is Bicep in `infra/`, modular, with environment parameter files that never contain secrets or a region. CI compiles it; nothing deploys it.

## Not decided (OPS-01, OPS-02)

Subscription and tenant, region and data residency, SKUs, redundancy (LRS/ZRS/GRS), HA mode, budget ceilings, alert destinations, support access, private endpoints versus service endpoints, RTO/RPO. The skeleton uses low-cost dev defaults that must be revisited.

## Consequences

- Choosing another Azure compute host (for example App Service or AKS) or another cloud requires a new ADR.
- Azure commercial hosting does not make the service suitable for FCI/CUI, classified data, FedRAMP or agency workloads (CLOUD-04).
- The PostgreSQL skeleton uses password authentication with the secret in Key Vault; Entra ID authentication with managed identity is the recommended follow-up.
