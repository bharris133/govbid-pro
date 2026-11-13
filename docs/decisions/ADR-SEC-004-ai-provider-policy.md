# ADR-SEC-004: AI provider policy, disabled by default

- Status: Accepted (D-03 strategy). Any pilot is pending AI-01; confidential data is pending AI-02.
- Date: 2026-11-13
- Source: Handoff v4 D-03, §9C, §9H

## Decision

- Provider-neutral contracts in Application: `IAiProvider`, `IAiPolicyGate`, `AiTaskService`. `AzureAiProvider` lives in Infrastructure and is deliberately unimplemented until AI-01 names the service, model, deployment, region and terms. No provider SDK types leak into Domain or DTOs.
- **Everything is off by default:** platform switch, provider (`None`), provider allowlist, feature allowlist and per-tenant consent. Startup fails if AI is enabled without a recorded `Ai01ApprovalReference` (the `Fake` provider is accepted only in the Testing environment).
- **Gate order** (fails closed, before any retrieval or provider call): tenant resolved → platform enabled → provider allowlisted → AI-01 reference present → feature allowlisted → tenant consent → inputs owned by the tenant and available → classification eligible for `AiAssist`.
- Eligibility: Public only. ConfidentialCommercial needs `DataHandling:ConfidentialCommercialAiApproved` (AI-02). Restricted/Prohibited are always denied.
- Prompt-injection defense: fixed application instructions; source text wrapped in a delimited data block with delimiter spoofing neutralized; no tools are given to the model.
- Output is labeled as a suggestion requiring human review, with factual claims marked unverified.
- One provider attempt per request with a timeout and no automatic retries (no duplicate billing on timeout).
- `ai_request_records` keep content-free metadata only (decision, reason, provider/model, template version, input IDs, highest classification, token counts). Provisional retention 90 days (D-02).
- No AI endpoint is exposed in P0.

## Consequences

Engineering tests cover AI-01 (no consent), AI-02 (cross-tenant input), AI-03 (classification), AI-04 (injection containment), AI-06 (outage/throttling/timeout) with a network-free fake provider. AI-05 (unsupported factual claims) needs output validation and is future work. These tests are not the owner approvals AI-01/AI-02.
