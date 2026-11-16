using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Common;
using GovBidPro.Application.Opportunities;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GovBidPro.Application.Dashboard;

public sealed record DiscoverySummaryDto(int OpenOpportunities, int DueWithin7Days, int NewLast7Days, int AmendedLast7Days);

public sealed record UpcomingDeadlineDto(Guid Id, string Title, string? Agency, ResponseType ResponseType, DateTimeOffset DueAt, string? DueOriginalText, bool IsLiveSource);

public sealed record SavedSearchSummaryDto(Guid Id, string Name, NotificationFrequency NotificationFrequency, int OpenMatches, int NewMatchesLast7Days);

/// <summary>Counts of the company's latest decision per opportunity.</summary>
public sealed record DecisionCountsDto(int Go, int NoGo, int Watch, int Undecided);

public sealed record DashboardDto(
    DateTimeOffset GeneratedAt,
    DiscoverySummaryDto Discovery,
    IReadOnlyList<UpcomingDeadlineDto> UpcomingDeadlines,
    IReadOnlyList<SavedSearchSummaryDto> SavedSearches,
    IReadOnlyList<SourceFreshnessDto> Sources,
    DecisionCountsDto Decisions);

/// <summary>
/// Discovery dashboard (P1.1): what is open, what is due soon, what is new, and how each of the
/// company's saved searches is doing. Opportunity data is public; saved searches are tenant-filtered.
/// </summary>
public sealed class DashboardService(IAppDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public const int MaxSavedSearchesSummarized = 20;
    private const int UpcomingDeadlineCount = 8;

    public async Task<DashboardDto> GetAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        var now = clock.GetUtcNow();
        var weekAhead = now.AddDays(7);
        var weekAgo = now.AddDays(-7);

        var open = db.Opportunities.AsNoTracking().Where(o => o.NoticeType != SourceNoticeType.AwardNotice && (o.Deadline.DueAt == null || o.Deadline.DueAt >= now));
        var discovery = new DiscoverySummaryDto(
            await open.CountAsync(cancellationToken),
            await open.CountAsync(o => o.Deadline.DueAt != null && o.Deadline.DueAt <= weekAhead, cancellationToken),
            await db.Opportunities.AsNoTracking().CountAsync(o => o.FirstIngestedAt >= weekAgo, cancellationToken),
            await db.Opportunities.AsNoTracking().CountAsync(o => o.RevisionNumber > 1 && o.LastRetrievedAt >= weekAgo, cancellationToken));

        var upcoming = await open
            .Where(o => o.Deadline.DueAt != null)
            .OrderBy(o => o.Deadline.DueAt)
            .ThenBy(o => o.Id)
            .Take(UpcomingDeadlineCount)
            .Select(o => new UpcomingDeadlineDto(o.Id, o.Title, o.Agency, o.ResponseType, o.Deadline.DueAt!.Value, o.Deadline.OriginalText, o.IsLiveSource))
            .ToListAsync(cancellationToken);

        var searches = await db.SavedSearches.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .OrderBy(s => s.Name)
            .Take(MaxSavedSearchesSummarized)
            .ToListAsync(cancellationToken);
        var summaries = new List<SavedSearchSummaryDto>(searches.Count);
        foreach (var search in searches)
        {
            var matches = OpportunityFilter.Apply(open, search.Criteria, now);
            summaries.Add(new SavedSearchSummaryDto(
                search.Id,
                search.Name,
                search.NotificationFrequency,
                await matches.CountAsync(cancellationToken),
                await matches.CountAsync(o => o.FirstIngestedAt >= weekAgo, cancellationToken)));
        }

        var sources = await db.SourceSyncStates.AsNoTracking()
            .OrderBy(s => s.SourceKey)
            .Select(s => new SourceFreshnessDto(s.SourceKey, s.IsLiveSource, s.LastSuccessfulSyncAt, s.LastAttemptAt, s.LastErrorCode))
            .ToListAsync(cancellationToken);

        var decisions = await db.OpportunityAssessments.AsNoTracking()
            .Where(a => a.TenantId == tenantId && !db.OpportunityAssessments.Any(b => b.TenantId == tenantId && b.OpportunityId == a.OpportunityId && b.Sequence > a.Sequence))
            .GroupBy(a => a.Decision)
            .Select(g => new { Decision = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        int CountOf(GoNoGoDecision decision) => decisions.Where(d => d.Decision == decision).Select(d => d.Count).SingleOrDefault();

        return new DashboardDto(
            now, discovery, upcoming, summaries, sources,
            new DecisionCountsDto(CountOf(GoNoGoDecision.Go), CountOf(GoNoGoDecision.NoGo), CountOf(GoNoGoDecision.Watch), CountOf(GoNoGoDecision.Undecided)));
    }
}
