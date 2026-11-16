using GovBidPro.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GovBidPro.Infrastructure.Persistence.Configurations;

internal sealed class CompanyProfileConfiguration : IEntityTypeConfiguration<CompanyProfile>
{
    public void Configure(EntityTypeBuilder<CompanyProfile> builder)
    {
        builder.ToTable("company_profiles");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Fit).HasJsonbConversion();
        builder.Property(p => p.UpdatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property<uint>("Version").IsRowVersion();
        builder.HasIndex(p => p.TenantId).IsUnique();
        builder.HasOne<Company>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OpportunityAssessmentConfiguration : IEntityTypeConfiguration<OpportunityAssessment>
{
    public void Configure(EntityTypeBuilder<OpportunityAssessment> builder)
    {
        builder.ToTable("opportunity_assessments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Band).HasMaxLength(16).IsRequired();
        builder.Property(a => a.RuleVersion).HasMaxLength(32).IsRequired();
        builder.Property(a => a.CriteriaJson).HasColumnType("jsonb").IsRequired();
        builder.Property(a => a.Decision).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.Rationale).HasMaxLength(OpportunityAssessment.MaxRationaleLength);
        builder.Property(a => a.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.HasIndex(a => new { a.TenantId, a.OpportunityId, a.Sequence }).IsUnique();
        builder.HasOne<Company>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Opportunity>().WithMany().HasForeignKey(a => a.OpportunityId).OnDelete(DeleteBehavior.Restrict);
    }
}
