using GovBidPro.Domain.Common;
using GovBidPro.Domain.Enums;

namespace GovBidPro.Domain.Entities;

/// <summary>Links an Identity user to a company with a company-scoped role.</summary>
public sealed class CompanyMembership : ITenantOwned
{
    private CompanyMembership()
    {
        UserId = string.Empty;
    }

    public CompanyMembership(Guid id, Guid tenantId, string userId, CompanyRole role, MembershipStatus status, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (tenantId == Guid.Empty)
        {
            throw new DomainException("Tenant id is required.");
        }

        Id = id;
        TenantId = tenantId;
        UserId = userId;
        Role = role;
        Status = status;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string UserId { get; private set; }

    public CompanyRole Role { get; private set; }

    public MembershipStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsActive => Status == MembershipStatus.Active;

    public void ChangeRole(CompanyRole role) => Role = role;

    public void Deprovision() => Status = MembershipStatus.Removed;
}
