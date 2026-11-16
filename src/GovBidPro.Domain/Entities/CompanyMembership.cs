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
        EmailNotifications = true;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string UserId { get; private set; }

    public CompanyRole Role { get; private set; }

    public MembershipStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsActive => Status == MembershipStatus.Active;

    /// <summary>Whether this member also gets notifications by email (in-app notifications are always kept).</summary>
    public bool EmailNotifications { get; private set; }

    public DateTimeOffset? RemovedAt { get; private set; }

    public void ChangeRole(CompanyRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new DomainException("Unknown role.");
        }

        Role = role;
    }

    public void Deprovision(DateTimeOffset at)
    {
        Status = MembershipStatus.Removed;
        RemovedAt = at;
    }

    /// <summary>A removed member who accepts a new invitation gets the same membership row back.</summary>
    public void Reactivate(CompanyRole role)
    {
        ChangeRole(role);
        Status = MembershipStatus.Active;
        RemovedAt = null;
    }

    public void SetEmailNotifications(bool enabled) => EmailNotifications = enabled;
}
