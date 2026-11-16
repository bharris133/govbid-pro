using GovBidPro.Domain.Enums;

namespace GovBidPro.Application.Auditing;

/// <summary>Security audit sink. Callers pass identifiers and reason codes only, never content.</summary>
public interface IAuditLog
{
    Task RecordAsync(string action, string resourceType, string? resourceId, AuditOutcome outcome, string reasonCode, CancellationToken cancellationToken);
}

public static class AuditActions
{
    public const string CompanyCreated = "company.created";
    public const string SavedSearchCreated = "saved_search.created";
    public const string SavedSearchUpdated = "saved_search.updated";
    public const string SavedSearchDeleted = "saved_search.deleted";
    public const string CompanyProfileUpdated = "company_profile.updated";
    public const string AssessmentRecorded = "assessment.recorded";
    public const string ProposalCreated = "proposal.created";
    public const string ProposalUpdated = "proposal.updated";
    public const string ProposalStatusChanged = "proposal.status_changed";
    public const string ProposalSectionChanged = "proposal.section_changed";
    public const string RequirementChanged = "proposal.requirement_changed";
    public const string ReviewRequested = "proposal.review_requested";
    public const string ReviewDecided = "proposal.review_decided";
    public const string DocumentUpload = "document.upload";
    public const string DocumentDownload = "document.download";
    public const string DocumentDelete = "document.delete";
    public const string DocumentRestore = "document.restore";
    public const string DocumentQuarantine = "document.quarantine";
    public const string DocumentPurge = "document.purge";
    public const string AccessDenied = "access.denied";
    public const string AiRequest = "ai.request";
    public const string OpportunityImport = "opportunity.import";
}
