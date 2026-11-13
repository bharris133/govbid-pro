using GovBidPro.Domain.Enums;

namespace GovBidPro.Application.Abstractions;

/// <summary>
/// The server-resolved tenant and actor for the current unit of work (handoff §5: tenant context
/// is resolved server-side, never taken from client input).
/// </summary>
public interface ITenantContext
{
    /// <summary>True when a verified company membership has been resolved for this request.</summary>
    bool HasTenant { get; }

    /// <summary>True only for trusted background jobs that operate across tenants explicitly.</summary>
    bool IsSystem { get; }

    Guid TenantId { get; }

    string? UserId { get; }

    CompanyRole? Role { get; }
}

/// <summary>Mutable holder populated once per request/job. Exposed as <see cref="ITenantContext"/>.</summary>
public sealed class TenantContextHolder : ITenantContext
{
    private Guid _tenantId;

    public bool HasTenant { get; private set; }

    public bool IsSystem { get; private set; }

    public Guid TenantId => HasTenant ? _tenantId : throw new InvalidOperationException("No tenant has been resolved for this context.");

    public string? UserId { get; private set; }

    public CompanyRole? Role { get; private set; }

    public void SetUser(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        UserId = userId;
    }

    public void SetTenant(Guid tenantId, string userId, CompanyRole role)
    {
        if (HasTenant || IsSystem)
        {
            throw new InvalidOperationException("Tenant context can only be set once.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        _tenantId = tenantId;
        UserId = userId;
        Role = role;
        HasTenant = true;
    }

    public void SetSystem()
    {
        if (HasTenant)
        {
            throw new InvalidOperationException("A tenant-scoped context cannot become a system context.");
        }

        IsSystem = true;
    }
}

public interface ICorrelationContext
{
    string CorrelationId { get; }
}
