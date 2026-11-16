using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Auditing;
using GovBidPro.Application.Common;
using GovBidPro.Domain.Common;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GovBidPro.Application.Collaboration;

public sealed record CommentCommand(string Body, Guid? RequirementId = null);

public sealed record CommentDto(Guid Id, Guid? RequirementId, string? RequirementReference, string AuthorUserId, string Author, string Body, DateTimeOffset CreatedAt, bool Deleted, bool CanDelete);

/// <summary>
/// Discussion on a proposal. Every member, including reviewers, can comment. Authors and team managers
/// can delete; a deleted comment keeps its place with the text removed.
/// </summary>
public sealed class CommentService(IAppDbContext db, ITenantContext tenant, IAuditLog audit, IUserDirectory users, NotificationService notifications, TimeProvider clock)
{
    public async Task<IReadOnlyList<CommentDto>> ListAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        var proposal = await FindProposalAsync(proposalId, cancellationToken);
        var userId = tenant.RequireUser();
        var rows = await db.Comments.AsNoTracking().Where(c => c.TenantId == proposal.TenantId && c.ProposalId == proposalId).OrderBy(c => c.CreatedAt).Take(1000).ToListAsync(cancellationToken);
        var requirementIds = rows.Where(c => c.RequirementId is not null).Select(c => c.RequirementId!.Value).Distinct().ToList();
        var references = await db.ProposalRequirements.AsNoTracking().Where(r => r.TenantId == proposal.TenantId && requirementIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Reference, cancellationToken);
        var names = await users.GetAsync(rows.Select(c => c.AuthorUserId).Distinct().ToList(), cancellationToken);
        var canModerate = tenant.Role >= CompanyRole.TeamManager;
        return rows.Select(c => new CommentDto(
            c.Id, c.RequirementId, c.RequirementId is { } rid && references.TryGetValue(rid, out var reference) ? reference : null, c.AuthorUserId, names.NameOf(c.AuthorUserId),
            c.Body, c.CreatedAt, c.DeletedAt is not null, c.DeletedAt is null && (canModerate || c.AuthorUserId == userId))).ToList();
    }

    public async Task<IReadOnlyList<CommentDto>> AddAsync(Guid proposalId, CommentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var userId = tenant.RequireUser();
        var proposal = await FindProposalAsync(proposalId, cancellationToken);
        string? requirementOwner = null;
        string? reference = null;
        if (command.RequirementId is { } requirementId)
        {
            var requirement = await db.ProposalRequirements.AsNoTracking().SingleOrDefaultAsync(r => r.Id == requirementId && r.TenantId == proposal.TenantId && r.ProposalId == proposalId, cancellationToken)
                ?? throw new RequestValidationException("comment.requirement_not_found", "That requirement is not part of this proposal.");
            requirementOwner = requirement.AssigneeUserId;
            reference = requirement.Reference;
        }

        Comment comment;
        try
        {
            comment = new Comment(Guid.NewGuid(), proposal.TenantId, proposalId, command.RequirementId, userId, command.Body, clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            throw new RequestValidationException("comment.invalid", ex.Message);
        }

        db.Comments.Add(comment);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.CommentChanged, "comment", comment.Id.ToString(), AuditOutcome.Success, "added", cancellationToken);

        // The comment text stays in the app; the notification only says where to look.
        var author = (await users.GetAsync([userId], cancellationToken)).NameOf(userId);
        var recipients = new[] { proposal.OwnerUserId, requirementOwner }.Where(id => id != userId);
        await notifications.PublishAsync(proposal.TenantId, recipients, NotificationKinds.Comment, $"New comment on {proposal.Title}", reference is null ? $"From {author}" : $"From {author} on requirement {reference}", $"/Proposals/Detail?id={proposalId}", cancellationToken);
        return await ListAsync(proposalId, cancellationToken);
    }

    public async Task DeleteAsync(Guid proposalId, Guid commentId, CancellationToken cancellationToken)
    {
        var userId = tenant.RequireUser();
        var proposal = await FindProposalAsync(proposalId, cancellationToken);
        var comment = await db.Comments.SingleOrDefaultAsync(c => c.Id == commentId && c.TenantId == proposal.TenantId && c.ProposalId == proposalId, cancellationToken)
            ?? throw new ResourceNotFoundException();
        if (comment.AuthorUserId != userId)
        {
            tenant.RequireRole(CompanyRole.TeamManager);
        }

        comment.Delete(userId, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.CommentChanged, "comment", commentId.ToString(), AuditOutcome.Success, "deleted", cancellationToken);
    }

    private async Task<Proposal> FindProposalAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        var proposal = await db.Proposals.AsNoTracking().SingleOrDefaultAsync(p => p.Id == proposalId && p.TenantId == tenantId, cancellationToken);
        if (proposal is null)
        {
            await audit.RecordAsync(AuditActions.AccessDenied, "proposal", proposalId.ToString(), AuditOutcome.Denied, "not_found_or_cross_tenant", cancellationToken);
            throw new ResourceNotFoundException();
        }

        return proposal;
    }
}
