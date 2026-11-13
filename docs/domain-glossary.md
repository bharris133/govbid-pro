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
