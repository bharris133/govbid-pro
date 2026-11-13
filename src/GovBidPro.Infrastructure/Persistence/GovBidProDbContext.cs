using GovBidPro.Application.Abstractions;
using GovBidPro.Domain.Entities;
using GovBidPro.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GovBidPro.Infrastructure.Persistence;

/// <summary>
/// EF Core context. Tenant-owned entities carry a fail-closed global query filter as defense in depth:
/// with no resolved tenant (and no explicit system context) they return no rows. Services still filter
/// by tenant explicitly; the filter is not the only control.
/// </summary>
public sealed class GovBidProDbContext(DbContextOptions<GovBidProDbContext> options, ITenantContext tenant)
    : IdentityDbContext<ApplicationUser>(options), IAppDbContext
{
    public DbSet<Company> Companies => Set<Company>();

    public DbSet<CompanyMembership> CompanyMemberships => Set<CompanyMembership>();

    public DbSet<Opportunity> Opportunities => Set<Opportunity>();

    public DbSet<OpportunityRevision> OpportunityRevisions => Set<OpportunityRevision>();

    public DbSet<SourceSyncState> SourceSyncStates => Set<SourceSyncState>();

    public DbSet<ImportRun> ImportRuns => Set<ImportRun>();

    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();

    public DbSet<StoredDocument> StoredDocuments => Set<StoredDocument>();

    public DbSet<RetentionPolicy> RetentionPolicies => Set<RetentionPolicy>();

    public DbSet<RetentionAction> RetentionActions => Set<RetentionAction>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<AiRequestRecord> AiRequestRecords => Set<AiRequestRecord>();

    /// <summary>
    /// Pinned so the model is identical whether built by the web host, tests or tooling.
    /// Must match IdentityOptions.Stores.SchemaVersion in Program.cs.
    /// </summary>
    protected override Version SchemaVersion => IdentitySchemaVersions.Version2;

    // Evaluated per query against this context instance (EF parameterizes context members).
    private bool TenantFilterBypassed => tenant.IsSystem;

    private Guid TenantFilterId => tenant.HasTenant ? tenant.TenantId : Guid.Empty;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);

        // Identity column sizes are set explicitly (matching IdentityOptions.Stores in Program.cs) so the
        // model does not depend on which host or tool builds the context.
        builder.Entity<ApplicationUser>().Property(u => u.PhoneNumber).HasMaxLength(256);
        builder.Entity<IdentityUserLogin<string>>(b =>
        {
            b.Property(l => l.LoginProvider).HasMaxLength(128);
            b.Property(l => l.ProviderKey).HasMaxLength(128);
        });
        builder.Entity<IdentityUserToken<string>>(b =>
        {
            b.Property(t => t.LoginProvider).HasMaxLength(128);
            b.Property(t => t.Name).HasMaxLength(128);
        });
        builder.ApplyConfigurationsFromAssembly(typeof(GovBidProDbContext).Assembly);

        builder.Entity<CompanyMembership>().HasQueryFilter(e => TenantFilterBypassed || e.TenantId == TenantFilterId);
        builder.Entity<SavedSearch>().HasQueryFilter(e => TenantFilterBypassed || e.TenantId == TenantFilterId);
        builder.Entity<StoredDocument>().HasQueryFilter(e => TenantFilterBypassed || e.TenantId == TenantFilterId);
        builder.Entity<RetentionAction>().HasQueryFilter(e => TenantFilterBypassed || e.TenantId == TenantFilterId);
        builder.Entity<AiRequestRecord>().HasQueryFilter(e => TenantFilterBypassed || e.TenantId == TenantFilterId);
    }
}
