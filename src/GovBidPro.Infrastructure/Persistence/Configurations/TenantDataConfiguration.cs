using System.Text.Json;
using GovBidPro.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GovBidPro.Infrastructure.Persistence.Configurations;

internal sealed class SavedSearchConfiguration : IEntityTypeConfiguration<SavedSearch>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<SavedSearch> builder)
    {
        builder.ToTable("saved_searches");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(120).IsRequired();
        builder.Property(s => s.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(s => s.NotificationFrequency).HasConversion<string>().HasMaxLength(32);
        builder.Property(s => s.Criteria)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<OpportunitySearchCriteria>(v, JsonOptions) ?? new OpportunitySearchCriteria(),
                new ValueComparer<OpportunitySearchCriteria>(
                    (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
                    v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(StringComparison.Ordinal),
                    v => JsonSerializer.Deserialize<OpportunitySearchCriteria>(JsonSerializer.Serialize(v, JsonOptions), JsonOptions)!));
        builder.Property<uint>("Version").IsRowVersion();
        builder.HasIndex(s => s.TenantId);
        builder.HasOne<Company>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StoredDocumentConfiguration : IEntityTypeConfiguration<StoredDocument>
{
    public void Configure(EntityTypeBuilder<StoredDocument> builder)
    {
        builder.ToTable("stored_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.FileName).HasMaxLength(200).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(128).IsRequired();
        builder.Property(d => d.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(d => d.StorageKey).HasMaxLength(300).IsRequired();
        builder.HasIndex(d => d.StorageKey).IsUnique();
        builder.Property(d => d.Classification).HasConversion<string>().HasMaxLength(48);
        builder.Property(d => d.DeclaredByUserId).HasMaxLength(450).IsRequired();
        builder.Property(d => d.Source).HasMaxLength(64).IsRequired();
        builder.Property(d => d.ScanStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.State).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.QuarantineReason).HasMaxLength(100);
        builder.Property(d => d.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property<uint>("Version").IsRowVersion();
        builder.HasIndex(d => new { d.TenantId, d.State });
        builder.HasIndex(d => new { d.State, d.RecoverableUntil });
        builder.HasOne<Company>().WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RetentionPolicy>().WithMany().HasForeignKey(d => d.RetentionPolicyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RetentionPolicyConfiguration : IEntityTypeConfiguration<RetentionPolicy>
{
    public void Configure(EntityTypeBuilder<RetentionPolicy> builder)
    {
        builder.ToTable("retention_policies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.PolicyKey).HasMaxLength(64).IsRequired();
        builder.Property(p => p.ApprovalStatus).HasMaxLength(200).IsRequired();
        builder.HasIndex(p => new { p.PolicyKey, p.Version }).IsUnique();
    }
}

internal sealed class RetentionActionConfiguration : IEntityTypeConfiguration<RetentionAction>
{
    public void Configure(EntityTypeBuilder<RetentionAction> builder)
    {
        builder.ToTable("retention_actions");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.ResourceType).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Classification).HasConversion<string>().HasMaxLength(48);
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.FailureReason).HasMaxLength(200);
        builder.Property(a => a.ActorUserId).HasMaxLength(450);
        builder.Property(a => a.CorrelationId).HasMaxLength(128).IsRequired();
        builder.HasIndex(a => new { a.TenantId, a.ResourceId });
        builder.HasOne<RetentionPolicy>().WithMany().HasForeignKey(a => a.PolicyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.ActorUserId).HasMaxLength(450);
        builder.Property(a => a.Action).HasMaxLength(64).IsRequired();
        builder.Property(a => a.ResourceType).HasMaxLength(64).IsRequired();
        builder.Property(a => a.ResourceId).HasMaxLength(128);
        builder.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.ReasonCode).HasMaxLength(128).IsRequired();
        builder.Property(a => a.CorrelationId).HasMaxLength(128).IsRequired();
        builder.HasIndex(a => new { a.TenantId, a.OccurredAt });
        builder.HasIndex(a => a.OccurredAt);
    }
}

internal sealed class AiRequestRecordConfiguration : IEntityTypeConfiguration<AiRequestRecord>
{
    public void Configure(EntityTypeBuilder<AiRequestRecord> builder)
    {
        builder.ToTable("ai_request_records");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.ActorUserId).HasMaxLength(450).IsRequired();
        builder.Property(a => a.Feature).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Purpose).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Decision).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.ReasonCode).HasMaxLength(128).IsRequired();
        builder.Property(a => a.Provider).HasMaxLength(64);
        builder.Property(a => a.Model).HasMaxLength(128);
        builder.Property(a => a.PromptTemplateVersion).HasMaxLength(64);
        builder.Property(a => a.HighestInputClassification).HasConversion<string>().HasMaxLength(48);
        builder.Property(a => a.ResponseStatus).HasMaxLength(32);
        builder.Property(a => a.CorrelationId).HasMaxLength(128).IsRequired();
        builder.HasIndex(a => new { a.TenantId, a.OccurredAt });
    }
}
