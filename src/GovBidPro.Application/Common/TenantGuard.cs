using GovBidPro.Application.Abstractions;
using GovBidPro.Domain.Enums;

namespace GovBidPro.Application.Common;

/// <summary>Service-level tenant and role checks (handoff §5, §9D). Never rely on UI controls.</summary>
public static class TenantGuard
{
    public static Guid RequireTenant(this ITenantContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.HasTenant)
        {
            throw new PolicyDeniedException("tenant.not_resolved");
        }

        return context.TenantId;
    }

    public static string RequireUser(this ITenantContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.UserId ?? throw new PolicyDeniedException("user.not_authenticated");
    }

    public static void RequireRole(this ITenantContext context, CompanyRole minimum)
    {
        context.RequireTenant();
        if (context.Role is null || context.Role < minimum)
        {
            throw new PolicyDeniedException("role.insufficient");
        }
    }
}
