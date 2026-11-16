# Domain glossary

| Term | Meaning in GovBid Pro |
|---|---|
| **Tenant / Company** | A customer organization. All company-owned data carries its `TenantId`. |
| **Membership** | A user's role in a company: Reviewer, ProposalWriter, TeamManager, CompanyAdmin. |
| **Platform administrator** | Operator role, separate from company roles; requires MFA; grants no tenant data access. |
| **Opportunity** | A sourced government notice. Global, public data with provenance. |
| **Source notice type** | The notice type as the source publishes it (e.g. presolicitation, solicitation, sources sought, award). Stored normalized plus the verbatim source value. |
| **Response type** | What a contractor would prepare: RFI, RFP, RFQ, Sources Sought, no response expected, or undetermined. A separate dimension from notice type; interpreted by versioned rules and overridable by users. |
| **RFI** | Request for Information: market research, non-binding, not an award event. |
| **RFP** | Request for Proposal: structured proposal with sections, volumes, evaluation criteria and format rules. |
| **RFQ** | Request for Quotation: price-focused, line items and delivery terms. |
| **Sources Sought** | Market research asking for capability statements; often used to assess set-aside potential. |
| **VetCert** | SBA veteran small business certification context. GovBid Pro may record a company's status with provenance; it never certifies eligibility. |
| **Revision** | An immutable snapshot recorded whenever a notice's content changes (created or amended). |
| **Fixture source** | Fictional demo notices (`[FIXTURE]`, `example.invalid` URLs, `isLiveSource=false`). Never presented as live government data. |
| **Deadline** | Response due instant (UTC) plus the source's original text and time zone. The solicitation is authoritative. |
| **Submission instructions** | The agency's stated submission method, shown to users. GovBid Pro never submits on a user's behalf. |
| **Classification** | Product routing label: Public, BusinessInternal, ConfidentialCommercial, RestrictedPendingReview, ProhibitedClassified. Not a legal determination. |
| **Quarantine** | Document state that blocks all access pending security review. |
| **Retention policy** | Versioned, configured period for a data class. Release 1 values are provisional (D-02) pending legal review (LEG-01). |
| **Legal hold** | Flag that suspends eligible destruction. |
| **SubmittedExternally** | Proposal state recording user-entered evidence that the user submitted elsewhere. |
| **Company fit profile** | What a company states about itself for scoring: NAICS codes, set-aside programs it says it qualifies for, capability keywords, preferred states and agencies, minimum days to respond. Self-declared; never verified by GovBid Pro. |
| **Fit score** | Rule-based 0-100 screening score (`fit-v1`): NAICS 30, set-aside 20, keywords 20, place 10, agency 10, time 10. Criteria without data are left out. Not a win probability. |
| **Assessment** | Immutable company-specific record of a fit score, its criteria and a Go/No-go/Watch decision with author, time, rationale and the notice revision scored. The sequence of assessments is the audit trail. |
| **Proposal** | A company's response to one opportunity. Snapshots the notice revision and deadline when started. One open proposal per opportunity per company. |
| **Stage** | Proposal status: Draft, In progress, In review, Approved, Ready for submission, Submitted (outside GovBid Pro), Won, Lost, Cancelled. The transitions are fixed; the display names are configurable (`Proposals:StageLabels`). |
| **Compliance matrix** | The list of requirements extracted by users from the solicitation (reference such as L.4.2, text, source location, mandatory flag, response section, owner, evidence and status). |
| **Compliance status** | Not started, In progress, Needs review, Compliant, or Exception. An Exception must explain itself in the evidence field. |
| **Review round** | The reviews requested since the proposal last entered In review. Approval needs at least one approval, no pending reviews and no changes requested in the current round. |
