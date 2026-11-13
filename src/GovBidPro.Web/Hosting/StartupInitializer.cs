using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Opportunities;
using GovBidPro.Application.Retention;
using GovBidPro.Infrastructure.Identity;
using GovBidPro.Infrastructure.Persistence;
using GovBidPro.Infrastructure.SamGov;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GovBidPro.Web.Hosting;

internal static class StartupInitializer
{
    /// <summary>
    /// Applies reviewed migrations (only when explicitly configured; production uses a pipeline step),
    /// synchronizes configured retention policies, ensures platform roles, and optionally imports
    /// fixture notices for local development.
    /// </summary>
    public static async Task InitializeGovBidProAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<TenantContextHolder>().SetSystem();
        var configuration = services.GetRequiredService<IConfiguration>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("GovBidPro.Startup");

        if (configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
        {
            await services.GetRequiredService<GovBidProDbContext>().Database.MigrateAsync();
        }

        await services.GetRequiredService<RetentionPolicyCatalog>().SynchronizeAsync(CancellationToken.None);

        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(PlatformRoles.PlatformAdmin))
        {
            await roles.CreateAsync(new IdentityRole(PlatformRoles.PlatformAdmin));
        }

        if (configuration.GetValue<bool>("OpportunitySources:Fixture:ImportOnStartup"))
        {
            var summary = await services.GetRequiredService<OpportunityImportService>()
                .RunAsync(services.GetRequiredService<FixtureOpportunitySource>(), maxPages: 20, CancellationToken.None);
            logger.LogInformation("Fixture import finished: {Status}, created {Created}, amended {Amended}, unchanged {Unchanged}.", summary.Status, summary.Created, summary.Amended, summary.Unchanged);
        }
    }
}
