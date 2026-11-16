using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Common;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GovBidPro.Application.Collaboration;

public static class NotificationKinds
{
    public const string Assignment = "assignment";
    public const string ReviewRequested = "review.requested";
    public const string ReviewDecided = "review.decided";
    public const string TaskAssigned = "task.assigned";
    public const string Comment = "comment";
    public const string SavedSearchAlert = "saved_search.alert";
    public const string MemberJoined = "member.joined";
}

public sealed record NotificationDto(Guid Id, string Kind, string Title, string? Body, string? Link, DateTimeOffset CreatedAt, bool IsRead);

public sealed record NotificationListDto(int Unread, IReadOnlyList<NotificationDto> Items);

public sealed record NotificationPreferencesDto(bool EmailNotifications, bool EmailAvailable);

/// <summary>
/// In-app notifications, with email as a second channel when a provider is configured and the member
/// has not opted out. Email problems never fail the action that caused the notification.
/// </summary>
public sealed class NotificationService(IAppDbContext db, ITenantContext tenant, IEmailSender email, IUserDirectory users, IOptions<CollaborationOptions> options, TimeProvider clock, ILogger<NotificationService> logger)
{
    /// <summary>Notifies active members of one company. Unknown, removed and duplicate recipients are skipped.</summary>
    public async Task PublishAsync(Guid tenantId, IEnumerable<string?> userIds, string kind, string title, string? body, string? link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        var wanted = userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!).Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
        {
            return;
        }

        // Explicit tenant filter: this also runs for a user who is just joining and has no tenant context yet.
        var recipients = await db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.Status == MembershipStatus.Active && wanted.Contains(m.UserId))
            .Select(m => new { m.UserId, m.EmailNotifications })
            .ToListAsync(cancellationToken);
        if (recipients.Count == 0)
        {
            return;
        }

        var now = clock.GetUtcNow();
        foreach (var recipient in recipients)
        {
            db.Notifications.Add(new Notification(Guid.NewGuid(), tenantId, recipient.UserId, kind, Truncate(title, 200) ?? kind, Truncate(body, 1000), link, now));
        }

        await db.SaveChangesAsync(cancellationToken);

        var settings = options.Value;
        if (!settings.EmailNotificationsEnabled || !email.IsEnabled)
        {
            return;
        }

        var byEmail = recipients.Where(r => r.EmailNotifications).Select(r => r.UserId).ToList();
        var directory = await users.GetAsync(byEmail, cancellationToken);
        var absolute = settings.AbsoluteLink(link);
        foreach (var userId in byEmail)
        {
            if (!directory.TryGetValue(userId, out var user))
            {
                continue;
            }

            var text = string.Join("\n\n", new[] { body, absolute is null ? "Sign in to GovBid Pro to see the details." : $"Open: {absolute}", "You get this email because you are a member of a company on GovBid Pro. You can turn off email notifications on the Notifications page." }.Where(part => !string.IsNullOrWhiteSpace(part)));
            try
            {
                await email.SendAsync(new EmailMessage(user.Email, Truncate(title, 200) ?? "GovBid Pro notification", text), cancellationToken);
            }
#pragma warning disable CA1031 // Email is best effort; the in-app notification is already saved.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                logger.LogWarning("Notification email ({Kind}) could not be sent: {Error}.", kind, ex.GetType().Name);
            }
        }
    }

    public async Task<NotificationListDto> ListMineAsync(bool unreadOnly, CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        var userId = tenant.RequireUser();
        var mine = db.Notifications.AsNoTracking().Where(n => n.TenantId == tenantId && n.UserId == userId);
        var unread = await mine.CountAsync(n => n.ReadAt == null, cancellationToken);
        var rows = await (unreadOnly ? mine.Where(n => n.ReadAt == null) : mine).OrderByDescending(n => n.CreatedAt).Take(100).ToListAsync(cancellationToken);
        return new NotificationListDto(unread, rows.Select(n => new NotificationDto(n.Id, n.Kind, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt is not null)).ToList());
    }

    public async Task<int> UnreadCountAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        var userId = tenant.RequireUser();
        return await db.Notifications.CountAsync(n => n.TenantId == tenantId && n.UserId == userId && n.ReadAt == null, cancellationToken);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        var userId = tenant.RequireUser();
        var notification = await db.Notifications.SingleOrDefaultAsync(n => n.Id == id && n.TenantId == tenantId && n.UserId == userId, cancellationToken)
            ?? throw new ResourceNotFoundException();
        notification.MarkRead(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> MarkAllReadAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        var userId = tenant.RequireUser();
        var now = clock.GetUtcNow();
        var unread = await db.Notifications.Where(n => n.TenantId == tenantId && n.UserId == userId && n.ReadAt == null).ToListAsync(cancellationToken);
        foreach (var notification in unread)
        {
            notification.MarkRead(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return unread.Count;
    }

    public async Task<NotificationPreferencesDto> GetPreferencesAsync(CancellationToken cancellationToken)
    {
        var membership = await MyMembershipAsync(tracked: false, cancellationToken);
        return new NotificationPreferencesDto(membership.EmailNotifications, options.Value.EmailNotificationsEnabled && email.IsEnabled);
    }

    public async Task<NotificationPreferencesDto> SetEmailPreferenceAsync(bool enabled, CancellationToken cancellationToken)
    {
        var membership = await MyMembershipAsync(tracked: true, cancellationToken);
        membership.SetEmailNotifications(enabled);
        await db.SaveChangesAsync(cancellationToken);
        return new NotificationPreferencesDto(membership.EmailNotifications, options.Value.EmailNotificationsEnabled && email.IsEnabled);
    }

    private async Task<CompanyMembership> MyMembershipAsync(bool tracked, CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        var userId = tenant.RequireUser();
        var query = tracked ? db.CompanyMemberships : db.CompanyMemberships.AsNoTracking();
        return await query.SingleAsync(m => m.TenantId == tenantId && m.UserId == userId, cancellationToken);
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..(max - 1)] + "…" : trimmed;
    }
}

/// <summary>Turns proposal assignments and reviews into notifications (replaces the P1.4 no-op).</summary>
public sealed class ProposalNotifier(NotificationService notifications, ITenantContext tenant) : Proposals.IProposalNotifier
{
    public Task AssignedAsync(Proposal proposal, string assigneeUserId, string what, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        return string.Equals(assigneeUserId, tenant.UserId, StringComparison.Ordinal)
            ? Task.CompletedTask
            : notifications.PublishAsync(proposal.TenantId, [assigneeUserId], NotificationKinds.Assignment, $"You were assigned: {what}", $"Proposal: {proposal.Title}", Link(proposal), cancellationToken);
    }

    public Task ReviewRequestedAsync(Proposal proposal, ProposalReview review, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(review);
        return notifications.PublishAsync(proposal.TenantId, [review.ReviewerUserId], NotificationKinds.ReviewRequested, $"Review requested: {proposal.Title}", review.Instructions, Link(proposal), cancellationToken);
    }

    public Task ReviewDecidedAsync(Proposal proposal, ProposalReview review, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(review);
        var outcome = review.Outcome == ReviewOutcome.Approved ? "approved" : "sent back with changes requested";
        return notifications.PublishAsync(proposal.TenantId, new[] { proposal.OwnerUserId, review.RequestedByUserId }.Where(id => id != review.ReviewerUserId), NotificationKinds.ReviewDecided, $"Review {outcome}: {proposal.Title}", review.Comment, Link(proposal), cancellationToken);
    }

    private static string Link(Proposal proposal) => $"/Proposals/Detail?id={proposal.Id}";
}
