using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Common;
using GovBidPro.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GovBidPro.Application.Companies;

public sealed record MemberDto(string UserId, string Email, CompanyRole Role, MembershipStatus Status, DateTimeOffset JoinedAt);

/// <summary>Company members, for assignment pickers and member administration. Tenant-scoped.</summary>
public sealed class MemberService(IAppDbContext db, ITenantContext tenant, IUserDirectory users)
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
