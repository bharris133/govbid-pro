using GovBidPro.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GovBidPro.Infrastructure.Persistence.Configurations;

internal sealed class ProposalConfiguration : IEntityTypeConfiguration<Proposal>
{
    public void Configure(EntityTypeBuilder<Proposal> builder)
    {
        builder.ToTable("proposals");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Title).HasMaxLength(Proposal.MaxTitleLength).IsRequired();
        builder.Property(p => p.ResponseType).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.DueTextSnapshot).HasMaxLength(200);
        builder.Property(p => p.OwnerUserId).HasMaxLength(450).IsRequired();
        builder.Property(p => p.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(p => p.SubmissionEvidence).HasMaxLength(Proposal.MaxEvidenceLength);
        builder.Property<uint>("Version").IsRowVersion();
        builder.HasIndex(p => new { p.TenantId, p.Status });
        builder.HasIndex(p => new { p.TenantId, p.OpportunityId });
        builder.HasOne<Company>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Opportunity>().WithMany().HasForeignKey(p => p.OpportunityId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProposalEventConfiguration : IEntityTypeConfiguration<ProposalEvent>
{
    public void Configure(EntityTypeBuilder<ProposalEvent> builder)
    {
        builder.ToTable("proposal_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.From).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.To).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.Note).HasMaxLength(1000);
        builder.Property(e => e.ActorUserId).HasMaxLength(450).IsRequired();
        builder.HasIndex(e => new { e.TenantId, e.ProposalId, e.OccurredAt });
        builder.HasOne<Proposal>().WithMany().HasForeignKey(e => e.ProposalId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProposalSectionConfiguration : IEntityTypeConfiguration<ProposalSection>
{
    public void Configure(EntityTypeBuilder<ProposalSection> builder)
    {
        builder.ToTable("proposal_sections");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Title).HasMaxLength(200).IsRequired();
        builder.Property(s => s.AssigneeUserId).HasMaxLength(450);
        builder.Property<uint>("Version").IsRowVersion();
        builder.HasIndex(s => new { s.TenantId, s.ProposalId, s.Order });
        builder.HasOne<Proposal>().WithMany().HasForeignKey(s => s.ProposalId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProposalRequirementConfiguration : IEntityTypeConfiguration<ProposalRequirement>
{
    public void Configure(EntityTypeBuilder<ProposalRequirement> builder)
    {
        builder.ToTable("proposal_requirements");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Reference).HasMaxLength(64).IsRequired();
        builder.Property(r => r.SourceLocator).HasMaxLength(200);
        builder.Property(r => r.Text).HasMaxLength(ProposalRequirement.MaxTextLength).IsRequired();
        builder.Property(r => r.Category).HasMaxLength(64);
        builder.Property(r => r.ResponseLocation).HasMaxLength(200);
        builder.Property(r => r.AssigneeUserId).HasMaxLength(450);
        builder.Property(r => r.Evidence).HasMaxLength(2000);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property<uint>("Version").IsRowVersion();
        builder.HasIndex(r => new { r.TenantId, r.ProposalId });
        builder.HasIndex(r => new { r.TenantId, r.AssigneeUserId });
        builder.HasOne<Proposal>().WithMany().HasForeignKey(r => r.ProposalId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProposalSection>().WithMany().HasForeignKey(r => r.SectionId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ProposalReviewConfiguration : IEntityTypeConfiguration<ProposalReview>
{
    public void Configure(EntityTypeBuilder<ProposalReview> builder)
    {
        builder.ToTable("proposal_reviews");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ReviewerUserId).HasMaxLength(450).IsRequired();
        builder.Property(r => r.RequestedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(r => r.Instructions).HasMaxLength(2000);
        builder.Property(r => r.Comment).HasMaxLength(2000);
        builder.Property(r => r.Outcome).HasConversion<string>().HasMaxLength(32);
        builder.Property<uint>("Version").IsRowVersion();
        builder.HasIndex(r => new { r.TenantId, r.ProposalId });
        builder.HasIndex(r => new { r.TenantId, r.ReviewerUserId, r.Outcome });
        builder.HasOne<Proposal>().WithMany().HasForeignKey(r => r.ProposalId).OnDelete(DeleteBehavior.Restrict);
    }
}
