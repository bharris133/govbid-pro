# ADR-SEC-001: Release 1 approved data scope

- Status: Accepted
- Date: 2026-11-13
- Source: Handoff v4 §9A, §9B, §9E, CLOUD-04

## Decision

Release 1 serves commercial government contractors only (track R1). In scope: public solicitation notices, business profiles, company-owned proposals and ordinary confidential commercial material.

Out of scope and actively blocked: FCI that a contract requires to be safeguarded, CUI, classified information and other legally restricted records.

Classifications (`DataClassification`): Public, BusinessInternal, ConfidentialCommercial, RestrictedPendingReview, ProhibitedClassified. They route data in the product; they are not legal determinations. Automated detection is a signal and cannot prove the absence of FCI/CUI.

Enforcement in P0:

- Uploads require a classification and two affirmations; declared Restricted/Prohibited/Public are rejected before content is read.
- Content is checked for common security and CUI markings and rejected before storage on a match.
- `StoredDocument` cannot be constructed with a Restricted or Prohibited classification.
- A company admin can quarantine a document, which blocks all access immediately.
- The prohibited-data notice is on `/DataPolicy`, the home page, the footer and the upload form.

## Consequences

- No R2 (defense) or R3 (agency) functionality exists, and feature flags cannot substitute for the separate readiness packages (§9E.1).
- Logical tenant separation is not represented as a CUI or agency security boundary.
- The product makes no FedRAMP, CMMC, Section 508 or VetCert claims, and never submits proposals on a user's behalf.
