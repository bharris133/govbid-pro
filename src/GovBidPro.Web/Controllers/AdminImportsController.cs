using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Common;
using GovBidPro.Application.Opportunities;
using GovBidPro.Domain.Entities;
using GovBidPro.Infrastructure.SamGov;
using GovBidPro.Web.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GovBidPro.Web.Controllers;

/// <summary>Platform operator endpoints. Require the PlatformAdmin role and an MFA sign-in.</summary>
[ApiController]
[Route("api/v1/admin/imports")]
[Authorize(Policy = Policies.PlatformAdminWithMfa)]
public sealed class AdminImportsController(IAppDbContext db, OpportunityImportService imports, FixtureOpportunitySource fixture, OpportunitySyncService sync, IOptions<OpportunitySyncOptions> syncOptions) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<ImportRun>> Recent(CancellationToken cancellationToken) =>
        await db.ImportRuns.AsNoTracking().OrderByDescending(r => r.StartedAt).Take(50).ToListAsync(cancellationToken);

    /// <summary>Per-source sync state: last success, cursor, backoff and last error code.</summary>
    [HttpGet("sources")]
    public async Task<IReadOnlyList<SourceSyncState>> Sources(CancellationToken cancellationToken) =>
        await db.SourceSyncStates.AsNoTracking().OrderBy(s => s.SourceKey).ToListAsync(cancellationToken);

    /// <summary>Runs one bounded sync of every enabled live source now. 409 when no live source is enabled.</summary>
    [HttpPost("sync")]
    public async Task<IReadOnlyList<SourceSyncResult>> RunSync(CancellationToken cancellationToken)
    {
        if (sync.SourceKeys.Count == 0)
        {
            throw new ConflictException("No live opportunity source is enabled.");
        }

        return await sync.RunAsync(syncOptions.Value.MaxPagesPerRun, cancellationToken);
    }

    /// <summary>Runs the clearly labeled fixture import.</summary>
    [HttpPost("fixture")]
    public Task<ImportRunSummary> RunFixture(CancellationToken cancellationToken) => imports.RunAsync(fixture, maxPages: 20, cancellationToken);
}
