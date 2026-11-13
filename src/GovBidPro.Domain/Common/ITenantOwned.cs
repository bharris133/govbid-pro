namespace GovBidPro.Domain.Common;

/// <summary>Marks an entity owned by exactly one company (tenant). Enforced by query filters and service checks.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
