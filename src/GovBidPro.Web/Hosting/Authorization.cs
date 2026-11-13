using GovBidPro.Application.Abstractions;
using GovBidPro.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;

namespace GovBidPro.Web.Hosting;

internal static class Policies
{
    /// <summary>Authenticated user with a server-resolved active company membership.</summary>
    public const string TenantMember = "TenantMember";

    /// <summary>Platform administrator who signed in with MFA (§9D: MFA for platform admins from first release).</summary>
    public const string PlatformAdminWithMfa = "PlatformAdminWithMfa";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(TenantMember, p => p.RequireAuthenticatedUser().AddRequirements(new TenantMemberRequirement()));
        options.AddPolicy(PlatformAdminWithMfa, p => p.RequireAuthenticatedUser().RequireRole(PlatformRoles.PlatformAdmin).RequireClaim("amr", "mfa"));
        options.FallbackPolicy = null;
    }
}

internal sealed class TenantMemberRequirement : IAuthorizationRequirement;

internal sealed class TenantMemberHandler(ITenantContext tenant) : AuthorizationHandler<TenantMemberRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantMemberRequirement requirement)
    {
        if (tenant.HasTenant)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
