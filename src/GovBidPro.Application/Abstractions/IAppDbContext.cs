using GovBidPro.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GovBidPro.Application.Abstractions;

/// <summary>Persistence port. Tenant-owned sets are additionally protected by fail-closed query filters.</summary>
public interface IAppDbContext
{
    DbSet<Company> Companies { get; }

    DbSet<CompanyMembership> CompanyMemberships { get; }

    DbSet<Opportunity> Opportunities { get; }

    DbSet<OpportunityRevision> OpportunityRevisions { get; }

    DbSet<SourceSyncState> SourceSyncStates { get; }

    DbSet<ImportRun> ImportRuns { get; }

    DbSet<SavedSearch> SavedSearches { get; }

    DbSet<StoredDocument> StoredDocuments { get; }

    DbSet<DocumentVersion> DocumentVersions { get; }

    DbSet<CompanyProfile> CompanyProfiles { get; }

    DbSet<OpportunityAssessment> OpportunityAssessments { get; }

    DbSet<Proposal> Proposals { get; }

    DbSet<ProposalEvent> ProposalEvents { get; }

    DbSet<ProposalSection> ProposalSections { get; }

    DbSet<ProposalRequirement> ProposalRequirements { get; }

    DbSet<ProposalReview> ProposalReviews { get; }

    DbSet<RetentionPolicy> RetentionPolicies { get; }

    DbSet<RetentionAction> RetentionActions { get; }

    DbSet<AuditEvent> AuditEvents { get; }

    DbSet<Notification> Notifications { get; }

    DbSet<WorkTask> WorkTasks { get; }

    DbSet<Comment> Comments { get; }

    DbSet<Invitation> Invitations { get; }

    DbSet<SavedSearchAlert> SavedSearchAlerts { get; }

    DbSet<AiRequestRecord> AiRequestRecords { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
