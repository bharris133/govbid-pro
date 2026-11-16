using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Auditing;
using GovBidPro.Application.Collaboration;
using GovBidPro.Application.Common;
using GovBidPro.Domain.Common;
using GovBidPro.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GovBidPro.Application.Companies;

public sealed record MemberDto(string UserId, string Email, CompanyRole Role, MembershipStatus Status, DateTimeOffset JoinedAt);

public sealed record ChangeRoleCommand(CompanyRole Role);

/// <summary>Company members, for assignment pickers and member administration. Tenant-scoped.</summary>
public sealed class MemberService(IAppDbContext db, ITenantContext tenant, IUserDirectory users, IAuditLog audit, IOptions<CollaborationOptions> options, TimeProvider clock)
{
    public async Task<IReadOnlyList<MemberDto>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        var memberships = await db.CompanyMemberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.Status == MembershipStatus.Active)
            .ToListAsync(cancellationToken);
        var names = await users.GetAsync(memberships.Select(m => m.UserId).ToList(), cancellationToken);
        return memberships
            .Select(m => new MemberDto(m.UserId, names.NameOf(m.UserId), m.Role, m.Status, m.CreatedAt))
            .OrderBy(m => m.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Changes a member's role. The company always keeps at least one company admin.</summary>
    public async Task<MemberDto> ChangeRoleAsync(string userId, ChangeRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        tenant.RequireRole(options.Value.MinimumRoleToManageMembers);
        var membership = await FindActiveAsync(userId, cancellationToken);
        if (!Enum.IsDefined(command.Role) || command.Role > tenant.Role || membership.Role > tenant.Role)
        {
            throw new PolicyDeniedException("role.insufficient");
        }

        await EnsureAnotherAdminAsync(membership, command.Role, cancellationToken);
        try
        {
            membership.ChangeRole(command.Role);
        }
        catch (DomainException ex)
        {
            throw new RequestValidationException("member.invalid", ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.MemberRoleChanged, "membership", membership.Id.ToString(), AuditOutcome.Success, $"role.{command.Role}", cancellationToken);
        var names = await users.GetAsync([userId], cancellationToken);
        return new MemberDto(userId, names.NameOf(userId), membership.Role, membership.Status, membership.CreatedAt);
    }

    /// <summary>
    /// Removes a member's access immediately. Their past work (assignments, comments, decisions) stays
    /// attributed to them; open assignments should be handed to someone else.
    /// </summary>
    public async Task RemoveAsync(string userId, CancellationToken cancellationToken)
    {
        tenant.RequireRole(options.Value.MinimumRoleToManageMembers);
        var membership = await FindActiveAsync(userId, cancellationToken);
        if (membership.Role > tenant.Role)
        {
            throw new PolicyDeniedException("role.insufficient");
        }

        await EnsureAnotherAdminAsync(membership, newRole: null, cancellationToken);
        var now = clock.GetUtcNow();
        membership.Deprovision(now);
        var pending = await db.ProposalReviews.Where(r => r.TenantId == membership.TenantId && r.ReviewerUserId == userId && r.Outcome == Domain.Entities.ReviewOutcome.Requested).ToListAsync(cancellationToken);
        foreach (var review in pending)
        {
            // A removed reviewer can never answer; the request must not block approval forever.
            review.Supersede(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.MemberRemoved, "membership", membership.Id.ToString(), AuditOutcome.Success, "ok", cancellationToken);
    }

    private async Task<Domain.Entities.CompanyMembership> FindActiveAsync(string userId, CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        return await db.CompanyMemberships.SingleOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId && m.Status == MembershipStatus.Active, cancellationToken)
            ?? throw new ResourceNotFoundException();
    }

    private async Task EnsureAnotherAdminAsync(Domain.Entities.CompanyMembership membership, CompanyRole? newRole, CancellationToken cancellationToken)
    {
        if (membership.Role != CompanyRole.CompanyAdmin || newRole == CompanyRole.CompanyAdmin)
        {
            return;
        }

        var otherAdmins = await db.CompanyMemberships.CountAsync(m => m.TenantId == membership.TenantId && m.Id != membership.Id && m.Status == MembershipStatus.Active && m.Role == CompanyRole.CompanyAdmin, cancellationToken);
        if (otherAdmins == 0)
        {
            throw new ConflictException("A company needs at least one company admin. Make someone else an admin first.");
        }
    }
}

public static class MembershipChecks
{
    /// <summary>Validates that a user id names an active member of the given company. Fails as a validation error, never revealing other tenants' users.</summary>
    public static async Task EnsureActiveMemberAsync(this IAppDbContext db, Guid tenantId, string? userId, string reasonCode, CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return;
        }

        var active = await db.CompanyMemberships.AsNoTracking().AnyAsync(m => m.TenantId == tenantId && m.UserId == userId && m.Status == MembershipStatus.Active, cancellationToken);
        if (!active)
        {
            throw new RequestValidationException(reasonCode, "The selected person is not an active member of your company.");
        }
    }
}
