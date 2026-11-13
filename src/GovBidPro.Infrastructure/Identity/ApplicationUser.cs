using Microsoft.AspNetCore.Identity;

namespace GovBidPro.Infrastructure.Identity;

/// <summary>ASP.NET Core Identity user. Company membership is modeled separately (CompanyMembership).</summary>
public sealed class ApplicationUser : IdentityUser
{
}

public static class PlatformRoles
{
    /// <summary>Platform operator role. Separate from company administration and requires MFA (§9D).</summary>
    public const string PlatformAdmin = "PlatformAdmin";
}
