using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Auditing;
using GovBidPro.Application.Common;
using GovBidPro.Domain.Common;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GovBidPro.Application.Companies;

public sealed record MembershipDto(Guid TenantId, string CompanyName, CompanyRole Role);

public sealed record CurrentUserDto(string UserId, Guid? TenantId, string? CompanyName, CompanyRole? Role, bool AiEnabledForTenant);

/// <summary>Company onboarding and membership resolution.</summary>
public sealed class CompanyService(IAppDbContext db, TimeProvider clock)
{
    /// <summary>Active memberships in active companies for a user. Used for server-side tenant resolution.</summary>
    public async Task<IReadOnlyList<MembershipDto>> GetActiveMembershipsAsync(string userId, CancellationToken cancellationToken)
    {
        return await (from m in db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking()
                      join c in db.Companies.AsNoTracking() on m.TenantId equals c.Id
                      where m.UserId == userId && m.Status == MembershipStatus.Active && c.Status == TenantStatus.Active
                      orderby c.DisplayName
                      select new MembershipDto(c.Id, c.DisplayName, m.Role)).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a company with the user as its first CompanyAdmin. P0 supports one company per user;
    /// multi-company membership and invitations are an open product question (§15).
    /// </summary>
    public async Task<MembershipDto> CreateCompanyAsync(string userId, string displayName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var existing = await GetActiveMembershipsAsync(userId, cancellationToken);
        if (existing.Count > 0)
        {
            throw new ConflictException("User already belongs to a company.");
        }

        var now = clock.GetUtcNow();
        Company company;
        try
        {
            company = new Company(Guid.NewGuid(), displayName, now);
        }
        catch (DomainException ex)
        {
            throw new RequestValidationException("company.invalid", ex.Message);
        }

        db.Companies.Add(company);
        db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), company.Id, userId, CompanyRole.CompanyAdmin, MembershipStatus.Active, now));
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), company.Id, userId, AuditActions.CompanyCreated, "company", company.Id.ToString(), AuditOutcome.Success, "ok", "onboarding", now));
        await db.SaveChangesAsync(cancellationToken);
        return new MembershipDto(company.Id, company.DisplayName, CompanyRole.CompanyAdmin);
    }
}
