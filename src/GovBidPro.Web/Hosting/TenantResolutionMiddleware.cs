using System.Security.Claims;
using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Companies;

namespace GovBidPro.Web.Hosting;

/// <summary>
/// Resolves the tenant server-side from the authenticated user's active membership (handoff §5).
/// Client-supplied tenant identifiers are never trusted. Users without a membership are sent to
/// onboarding for page requests; API requests receive 403 from the TenantMember policy.
/// </summary>
internal sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    private static readonly string[] TenantPagePrefixes = ["/Opportunities", "/Documents"];

    public async Task InvokeAsync(HttpContext context, TenantContextHolder holder, CompanyService companies)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (context.User.Identity?.IsAuthenticated == true && userId is not null)
        {
            holder.SetUser(userId);
            var memberships = await companies.GetActiveMembershipsAsync(userId, context.RequestAborted);

            // P0: exactly one active membership. Multi-company selection is an open product question (§15).
            if (memberships.Count == 1)
            {
                var membership = memberships[0];
                holder.SetTenant(membership.TenantId, userId, membership.Role);
            }
            else if (memberships.Count == 0 && TenantPagePrefixes.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.Redirect("/Onboarding/CreateCompany");
                return;
            }
        }

        await next(context);
    }
}
