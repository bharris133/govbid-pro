using System.Security.Cryptography;
using System.Text;
using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Auditing;
using GovBidPro.Application.Common;
using GovBidPro.Domain.Common;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GovBidPro.Application.Collaboration;

public sealed record InviteCommand(string Email, CompanyRole Role);

public sealed record InvitationDto(Guid Id, string Email, CompanyRole Role, InvitationState State, string InvitedBy, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

/// <summary>
/// The accept path contains the one-time secret. It is returned once, to the inviter, so the invitation
/// can still be passed on when email is not configured; it is never stored or logged.
/// </summary>
public sealed record InvitationCreatedDto(InvitationDto Invitation, string AcceptPath, bool EmailSent);

public sealed record InvitationPreviewDto(string CompanyName, CompanyRole Role, string Email, InvitationState State, DateTimeOffset ExpiresAt);

/// <summary>
/// Invitations to join a company (open question 2). Defaults: company admins invite, invitations last
/// seven days, are single use and bound to the invited email address. A user belongs to one company
/// at a time (open question 1), so an account that is already in a company cannot accept.
/// </summary>
public sealed class InvitationService(IAppDbContext db, ITenantContext tenant, IAuditLog audit, IUserDirectory users, IEmailSender email, NotificationService notifications, IOptions<CollaborationOptions> options, TimeProvider clock, ILogger<InvitationService> logger)
{
    private CollaborationOptions Settings => options.Value;

    public async Task<IReadOnlyList<InvitationDto>> ListAsync(CancellationToken cancellationToken)
    {
        tenant.RequireRole(Settings.MinimumRoleToInvite);
        var tenantId = tenant.TenantId;
        var rows = await db.Invitations.AsNoTracking().Where(i => i.TenantId == tenantId).OrderByDescending(i => i.CreatedAt).Take(200).ToListAsync(cancellationToken);
        var names = await users.GetAsync(rows.Select(i => i.InvitedByUserId).Distinct().ToList(), cancellationToken);
        var now = clock.GetUtcNow();
        return rows.Select(i => ToDto(i, names.NameOf(i.InvitedByUserId), now)).ToList();
    }

    public async Task<InvitationCreatedDto> InviteAsync(InviteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        tenant.RequireRole(Settings.MinimumRoleToInvite);
        var tenantId = tenant.TenantId;
        var userId = tenant.RequireUser();
        if (!Enum.IsDefined(command.Role) || command.Role > tenant.Role)
        {
            throw new RequestValidationException("invitation.invalid_role", "Choose a role no higher than your own.");
        }

        var now = clock.GetUtcNow();
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        Invitation invitation;
        try
        {
            invitation = new Invitation(Guid.NewGuid(), tenantId, command.Email, command.Role, HashToken(token), userId, now, now.AddDays(Settings.InvitationExpiryDays));
        }
        catch (DomainException ex)
        {
            throw new RequestValidationException("invitation.invalid", ex.Message);
        }

        var existingAccount = await users.FindByEmailAsync(invitation.Email, cancellationToken);
        if (existingAccount is not null && await db.CompanyMemberships.AnyAsync(m => m.TenantId == tenantId && m.UserId == existingAccount.UserId && m.Status == MembershipStatus.Active, cancellationToken))
        {
            throw new ConflictException("That person is already a member of your company.");
        }

        var pending = await db.Invitations.Where(i => i.TenantId == tenantId && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now).ToListAsync(cancellationToken);
        foreach (var earlier in pending.Where(i => i.NormalizedEmail == invitation.NormalizedEmail))
        {
            // Inviting the same address again replaces the earlier invitation.
            earlier.Revoke(now);
        }

        if (pending.Count(i => i.RevokedAt is null) >= Settings.MaxPendingInvitationsPerCompany)
        {
            throw new ConflictException("Your company has too many open invitations. Revoke some first.");
        }

        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.InvitationCreated, "invitation", invitation.Id.ToString(), AuditOutcome.Success, $"role.{invitation.Role}", cancellationToken);

        var acceptPath = $"/Invitations/Accept?token={token}";
        var sent = false;
        var absolute = Settings.AbsoluteLink(acceptPath);
        if (email.IsEnabled && absolute is not null)
        {
            var company = await db.Companies.AsNoTracking().Where(c => c.Id == tenantId).Select(c => c.DisplayName).SingleAsync(cancellationToken);
            var body = $"You have been invited to join {company} on GovBid Pro as {RoleLabel(invitation.Role)}.\n\nAccept the invitation: {absolute}\n\nSign in or create an account with this email address ({invitation.Email}) first. The invitation expires on {invitation.ExpiresAt.UtcDateTime:yyyy-MM-dd} (UTC) and can be used once. If you did not expect this, ignore this email.";
            try
            {
                await email.SendAsync(new EmailMessage(invitation.Email, $"Invitation to join {company} on GovBid Pro", body), cancellationToken);
                sent = true;
            }
#pragma warning disable CA1031 // The invitation exists; the inviter is told the email did not go out and can share the link.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                logger.LogWarning("Invitation email could not be sent: {Error}.", ex.GetType().Name);
            }
        }

        var names = await users.GetAsync([userId], cancellationToken);
        return new InvitationCreatedDto(ToDto(invitation, names.NameOf(userId), now), acceptPath, sent);
    }

    public async Task RevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        tenant.RequireRole(Settings.MinimumRoleToInvite);
        var tenantId = tenant.TenantId;
        var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.Id == id && i.TenantId == tenantId, cancellationToken) ?? throw new ResourceNotFoundException();
        var now = clock.GetUtcNow();
        if (invitation.StateAt(now) != InvitationState.Pending)
        {
            throw new ConflictException("Only a pending invitation can be revoked.");
        }

        invitation.Revoke(now);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.InvitationRevoked, "invitation", id.ToString(), AuditOutcome.Success, "ok", cancellationToken);
    }

    /// <summary>What an invitation offers, for the accept page. Unknown tokens look the same as any other miss.</summary>
    public async Task<InvitationPreviewDto?> PreviewAsync(string? token, CancellationToken cancellationToken)
    {
        var invitation = await FindByTokenAsync(token, tracked: false, cancellationToken);
        if (invitation is null)
        {
            return null;
        }

        var company = await db.Companies.AsNoTracking().Where(c => c.Id == invitation.TenantId).Select(c => c.DisplayName).SingleAsync(cancellationToken);
        return new InvitationPreviewDto(company, invitation.Role, invitation.Email, invitation.StateAt(clock.GetUtcNow()), invitation.ExpiresAt);
    }

    /// <summary>
    /// Accepts an invitation for a signed-in account. Runs before the user has a company, so it reads
    /// across tenants by token hash only and writes only the invited company's membership.
    /// </summary>
    public async Task<Guid> AcceptAsync(string userId, string? token, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var invitation = await FindByTokenAsync(token, tracked: true, cancellationToken) ?? throw new ResourceNotFoundException();
        var now = clock.GetUtcNow();
        var account = (await users.GetAsync([userId], cancellationToken)).GetValueOrDefault(userId) ?? throw new ResourceNotFoundException();

        async Task<Exception> DenyAsync(string reason, Exception error)
        {
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), invitation.TenantId, userId, AuditActions.InvitationAccepted, "invitation", invitation.Id.ToString(), AuditOutcome.Denied, reason, "invitation", now));
            await db.SaveChangesAsync(cancellationToken);
            return error;
        }

        if (invitation.StateAt(now) != InvitationState.Pending)
        {
            throw await DenyAsync("invitation.not_pending", new ConflictException("This invitation is no longer valid. Ask your company admin for a new one."));
        }

        if (!string.Equals(account.Email.Trim().ToUpperInvariant(), invitation.NormalizedEmail, StringComparison.Ordinal))
        {
            throw await DenyAsync("invitation.email_mismatch", new PolicyDeniedException("invitation.email_mismatch"));
        }

        if (Settings.RequireConfirmedEmailToAcceptInvitation && !account.EmailConfirmed)
        {
            throw await DenyAsync("invitation.email_not_confirmed", new PolicyDeniedException("invitation.email_not_confirmed"));
        }

        var memberships = await db.CompanyMemberships.IgnoreQueryFilters().Where(m => m.UserId == userId).ToListAsync(cancellationToken);
        if (memberships.Any(m => m.Status == MembershipStatus.Active))
        {
            throw await DenyAsync("invitation.already_in_company", new ConflictException("Your account already belongs to a company. An account can be in one company at a time."));
        }

        try
        {
            invitation.Accept(userId, account.Email, now);
        }
        catch (DomainException ex)
        {
            throw new ConflictException(ex.Message);
        }

        var membership = memberships.SingleOrDefault(m => m.TenantId == invitation.TenantId);
        if (membership is null)
        {
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), invitation.TenantId, userId, invitation.Role, MembershipStatus.Active, now));
        }
        else
        {
            membership.Reactivate(invitation.Role);
        }

        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), invitation.TenantId, userId, AuditActions.InvitationAccepted, "invitation", invitation.Id.ToString(), AuditOutcome.Success, $"role.{invitation.Role}", "invitation", now));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException("This invitation was just used. Reload the page.");
        }

        await notifications.PublishAsync(invitation.TenantId, [invitation.InvitedByUserId], NotificationKinds.MemberJoined, $"{account.Email} joined your company", $"Role: {RoleLabel(invitation.Role)}", "/Company", cancellationToken);
        return invitation.TenantId;
    }

    public static string HashToken(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));

    internal static string RoleLabel(CompanyRole role) => role switch
    {
        CompanyRole.CompanyAdmin => "company admin",
        CompanyRole.TeamManager => "team manager",
        CompanyRole.ProposalWriter => "proposal writer",
        _ => "reviewer",
    };

    private async Task<Invitation?> FindByTokenAsync(string? token, bool tracked, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
        {
            return null;
        }

        var hash = HashToken(token);
        var query = db.Invitations.IgnoreQueryFilters();
        return await (tracked ? query : query.AsNoTracking()).SingleOrDefaultAsync(i => i.TokenHash == hash, cancellationToken);
    }

    private static InvitationDto ToDto(Invitation i, string invitedBy, DateTimeOffset now) =>
        new(i.Id, i.Email, i.Role, i.StateAt(now), invitedBy, i.CreatedAt, i.ExpiresAt);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
