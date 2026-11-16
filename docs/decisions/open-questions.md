# Open product questions

These do not block P0 coding. Safe defaults are in place; each needs a product answer before the related milestone ships.

| # | Question | Safe default in P0 | Needed by |
|---|---|---|---|
| 1 | Can one user belong to several companies, and how do they switch? | One active membership per user; users with several memberships get no tenant and a 403 until selection is designed. | P1 (invitations) |
| 2 | Invitation lifecycle: who can invite, expiry, re-invite, deprovision behavior | Not built; first user of a company becomes its CompanyAdmin. | P1 |
| 3 | Final proposal lifecycle terminology (§7.3) | **Release 1 default (P1.4):** states Draft → In progress → In review → Approved → Ready for submission → Submitted (outside GovBid Pro) → Won/Lost, plus Cancelled. Display names are configuration (`Proposals:StageLabels`, e.g. `"InReview": "Pink team"`); transitions stay fixed in code. Approved and later stages need TeamManager+ (`Proposals:MinimumRoleForFinalStages`); owners cannot self-review (`Proposals:AllowOwnerSelfReview`); mandatory items must be resolved before Ready for submission (`Proposals:RequireMandatoryRequirementsResolved`). | Product owner to confirm names |
| 4 | Which non-federal procurement sources to support | Adapter interface only; no other sources. | P2 |
| 5 | Notification frequency options and channels | Saved searches store `None/Daily/Weekly`; no delivery yet. | P1.6 |
| 6 | Response-type interpretation rules beyond explicit wording | Mapping rule v1 only maps explicit "RFI/RFQ/RFP" wording; everything else is `Undetermined` for a user to decide. | P1.1 |
| 7 | Malware scanning product and cost | Not configured; downloads denied until scanned. | P1.5 |
| 8 | Email provider for confirmations and alerts | None; production requires one. | Before production |
