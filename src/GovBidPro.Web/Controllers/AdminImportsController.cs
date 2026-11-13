using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Opportunities;
using GovBidPro.Domain.Entities;
using GovBidPro.Infrastructure.SamGov;
using GovBidPro.Web.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GovBidPro.Web.Controllers;

/// <summary>Platform operator endpoints. Require the PlatformAdmin role and an MFA sign-in.</summary>
[ApiController]
[Route("api/v1/admin/imports")]
[Authorize(Policy = Policies.PlatformAdminWithMfa)]
public sealed class AdminImportsController(IAppDbContext db, OpportunityImportService imports, FixtureOpportunitySource fixture) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<ImportRun>> Recent(CancellationToken cancellationToken) =>
        await db.ImportRuns.AsNoTracking().OrderByDescending(r => r.StartedAt).Take(50).ToListAsync(cancellationToken);

    /// <summary>Runs the clearly labeled fixture import. The SAM.gov adapter is pending verification (P1.2).</summary>
    [HttpPost("fixture")]
    public Task<ImportRunSummary> RunFixture(CancellationToken cancellationToken) => imports.RunAsync(fixture, maxPages: 20, cancellationToken);
}
