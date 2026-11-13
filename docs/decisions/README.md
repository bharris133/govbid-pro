# Architecture decision records

| ADR | Title | Status |
|---|---|---|
| [ADR-0001](ADR-0001-modular-monolith-stack.md) | Modular monolith on .NET 10, Razor Pages and strict TypeScript | Accepted |
| [ADR-0002](ADR-0002-azure-hosting.md) | Azure hosting with Container Apps, PostgreSQL Flexible Server, Blob Storage and Key Vault | Accepted (D-01); production configuration pending OPS-01 |
| [ADR-0003](ADR-0003-configurable-retention.md) | Configurable, versioned retention policies | Accepted (D-02 provisional); pending LEG-01 |
| [ADR-SEC-001](ADR-SEC-001-approved-data-scope.md) | Release 1 approved data scope | Accepted |
| [ADR-SEC-002](ADR-SEC-002-tenant-identity-authorization.md) | Tenant identity and authorization | Accepted |
| [ADR-SEC-003](ADR-SEC-003-storage-classification-flow.md) | Storage and classification flow | Accepted |
| [ADR-SEC-004](ADR-SEC-004-ai-provider-policy.md) | AI provider policy, disabled by default | Accepted (D-03 strategy); pilot pending AI-01, confidential data pending AI-02 |

Choosing a production cloud other than Azure, or an Azure host other than Container Apps, requires a new ADR (handoff CLOUD-02).

Also see [open-questions.md](open-questions.md) and [../production-gates.md](../production-gates.md).
