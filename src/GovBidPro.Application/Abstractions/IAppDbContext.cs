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

    DbSet<RetentionPolicy> RetentionPolicies { get; }

    DbSet<RetentionAction> RetentionActions { get; }

    DbSet<AuditEvent> AuditEvents { get; }

    DbSet<AiRequestRecord> AiRequestRecords { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
