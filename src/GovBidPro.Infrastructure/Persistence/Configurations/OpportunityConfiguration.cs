using GovBidPro.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GovBidPro.Infrastructure.Persistence.Configurations;

internal sealed class OpportunityConfiguration : IEntityTypeConfiguration<Opportunity>
{
    public void Configure(EntityTypeBuilder<Opportunity> builder)
    {
        builder.ToTable("opportunities");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.SourceKey).HasMaxLength(64).IsRequired();
        builder.Property(o => o.ExternalId).HasMaxLength(256).IsRequired();
        builder.HasIndex(o => new { o.SourceKey, o.ExternalId }).IsUnique();
        builder.Property(o => o.Title).HasMaxLength(500).IsRequired();
        builder.Property(o => o.SolicitationNumber).HasMaxLength(128);
        builder.Property(o => o.Agency).HasMaxLength(300);
        builder.Property(o => o.Office).HasMaxLength(300);
        builder.Property(o => o.NoticeType).HasConversion<string>().HasMaxLength(64);
        builder.Property(o => o.SourceNoticeTypeRaw).HasMaxLength(100);
        builder.Property(o => o.ResponseType).HasConversion<string>().HasMaxLength(32);
        builder.Property(o => o.ResponseTypeBasis).HasConversion<string>().HasMaxLength(32);
        builder.Property(o => o.ResponseTypeRuleVersion).HasMaxLength(32);
        builder.Property(o => o.NaicsCode).HasMaxLength(10);
        builder.Property(o => o.PscCode).HasMaxLength(10);
        builder.Property(o => o.SetAside).HasMaxLength(100);
        builder.Property(o => o.PlaceOfPerformanceState).HasMaxLength(10);
        builder.Property(o => o.PlaceOfPerformanceCountry).HasMaxLength(10);
        builder.Property(o => o.SubmissionInstructions).HasMaxLength(4000);
        builder.Property(o => o.SourceUrl).HasMaxLength(2000);
        builder.Property(o => o.ContentHash).HasMaxLength(64).IsRequired();
        builder.OwnsOne(o => o.Deadline, d =>
        {
            d.Property(x => x.DueAt).HasColumnName("response_due_at");
            d.Property(x => x.OriginalText).HasColumnName("response_due_original_text").HasMaxLength(200);
            d.Property(x => x.SourceTimeZone).HasColumnName("response_due_time_zone").HasMaxLength(64);
        });
        builder.Navigation(o => o.Deadline).IsRequired();
        builder.HasMany(o => o.Revisions).WithOne().HasForeignKey(r => r.OpportunityId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Revisions).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(o => o.PostedAt);
        builder.HasIndex(o => o.NaicsCode);
        builder.HasIndex(o => o.NoticeType);
    }
}

internal sealed class OpportunityRevisionConfiguration : IEntityTypeConfiguration<OpportunityRevision>
{
    public void Configure(EntityTypeBuilder<OpportunityRevision> builder)
    {
        builder.ToTable("opportunity_revisions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ChangeKind).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(r => r.SnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(r => new { r.OpportunityId, r.RevisionNumber }).IsUnique();
    }
}

internal sealed class SourceSyncStateConfiguration : IEntityTypeConfiguration<SourceSyncState>
{
    public void Configure(EntityTypeBuilder<SourceSyncState> builder)
    {
        builder.ToTable("source_sync_states");
        builder.HasKey(s => s.SourceKey);
        builder.Property(s => s.SourceKey).HasMaxLength(64);
        builder.Property(s => s.Cursor).HasMaxLength(1000);
        builder.Property(s => s.LastErrorCode).HasMaxLength(64);
        builder.Property(s => s.LastErrorMessage).HasMaxLength(500);
    }
}

internal sealed class ImportRunConfiguration : IEntityTypeConfiguration<ImportRun>
{
    public void Configure(EntityTypeBuilder<ImportRun> builder)
    {
        builder.ToTable("import_runs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.SourceKey).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.StartingCursor).HasMaxLength(1000);
        builder.Property(r => r.ErrorCode).HasMaxLength(64);
        builder.HasIndex(r => new { r.SourceKey, r.StartedAt });
    }
}
