using GovBidPro.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GovBidPro.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.UserId).HasMaxLength(450).IsRequired();
        builder.Property(n => n.Kind).HasMaxLength(64).IsRequired();
        builder.Property(n => n.Title).HasMaxLength(200).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(1000);
        builder.Property(n => n.Link).HasMaxLength(300);
        builder.HasIndex(n => new { n.TenantId, n.UserId, n.ReadAt, n.CreatedAt });
        builder.HasOne<Company>().WithMany().HasForeignKey(n => n.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WorkTaskConfiguration : IEntityTypeConfiguration<WorkTask>
{
    public void Configure(EntityTypeBuilder<WorkTask> builder)
    {
        builder.ToTable("tasks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Title).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(2000);
        builder.Property(t => t.AssigneeUserId).HasMaxLength(450);
        builder.Property(t => t.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property<uint>("Version").IsRowVersion();
        builder.HasIndex(t => new { t.TenantId, t.AssigneeUserId, t.Status });
        builder.HasIndex(t => new { t.TenantId, t.ProposalId });
        builder.HasOne<Company>().WithMany().HasForeignKey(t => t.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Proposal>().WithMany().HasForeignKey(t => t.ProposalId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("comments");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.AuthorUserId).HasMaxLength(450).IsRequired();
        builder.Property(c => c.Body).HasMaxLength(Comment.MaxBodyLength).IsRequired();
        builder.Property(c => c.DeletedByUserId).HasMaxLength(450);
        builder.HasIndex(c => new { c.TenantId, c.ProposalId, c.CreatedAt });
        builder.HasOne<Proposal>().WithMany().HasForeignKey(c => c.ProposalId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProposalRequirement>().WithMany().HasForeignKey(c => c.RequirementId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("invitations");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Email).HasMaxLength(256).IsRequired();
        builder.Property(i => i.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(i => i.Role).HasConversion<string>().HasMaxLength(32);
        builder.Property(i => i.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(i => i.InvitedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(i => i.AcceptedByUserId).HasMaxLength(450);
        builder.Property<uint>("Version").IsRowVersion();
        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => new { i.TenantId, i.NormalizedEmail });
        builder.HasOne<Company>().WithMany().HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SavedSearchAlertConfiguration : IEntityTypeConfiguration<SavedSearchAlert>
{
    public void Configure(EntityTypeBuilder<SavedSearchAlert> builder)
    {
        builder.ToTable("saved_search_alerts");
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => new { a.SavedSearchId, a.OpportunityId }).IsUnique();
        builder.HasIndex(a => a.TenantId);
        builder.HasOne<SavedSearch>().WithMany().HasForeignKey(a => a.SavedSearchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Opportunity>().WithMany().HasForeignKey(a => a.OpportunityId).OnDelete(DeleteBehavior.Cascade);
    }
}
