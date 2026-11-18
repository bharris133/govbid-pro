# ADR-0004: Cloud portability

- Status: Accepted (owner rule, 2026-11-15)
- Relates to: [ADR-0002](ADR-0002-azure-hosting.md)

## Context

Azure is the current host (D-01). The owner may move to a more secure or cheaper provider later and wants the change to be easy.
The owner reported an Azure outage in the week before this decision, which raised the concern.

## Decision

- Keep the runtime to standard containers and standard PostgreSQL. No provider-specific database features.
- Every cloud service (object storage, key storage, email, malware scanning, scheduling, AI) is used through an
  application-layer port. Adapters, including all Azure SDK usage, live only in `GovBidPro.Infrastructure`.
- Each port has a local or provider-neutral adapter so the product runs with no cloud account at all.
- The adapter is chosen by configuration and validated at startup.
- `CloudPortabilityTests` enforces the boundary in CI.
- Prefer provider-neutral protocols where they exist (SMTP for email, clamd for scanning, PostgreSQL wire protocol, OCI images).

## Consequences

- Moving provider costs one adapter per service that has no neutral adapter, new infrastructure code and a data migration.
  The steps are in [../cloud-portability.md](../cloud-portability.md).
- Azure-only conveniences (for example Defender for Storage event-driven scanning or Service Bus) are not used directly.
  If one is ever adopted it goes behind a port with a neutral alternative. Defender for Storage malware scanning was
  adopted this way in [ADR-0005](ADR-0005-azure-defender-malware-scanning.md), with ClamAV as the neutral alternative.
- This supersedes the sentence in ADR-0002 that another cloud requires a new ADR for the *application*; a hosting change
  still needs an ADR recording the new provider's security and compliance review.
