using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Opportunities;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GovBidPro.Application.Collaboration;

public sealed record AlertRunSummary(int SearchesChecked, int AlertsSent, int OpportunitiesReported);

/// <summary>
/// Scheduled saved-search alerts (open question 5). For each search whose Daily or Weekly period has
/// passed, tells the person who saved it about matching notices that arrived since the last check.
/// Each notice is reported at most once per search. Runs in the system context.
/// </summary>
public sealed class SavedSearchAlertService(IAppDbContext db, ITenantContext context, NotificationService notifications, IOptions<CollaborationOptions> options, TimeProvider clock, ILogger<SavedSearchAlertService> logger)
{
    public async Task<AlertRunSummary> RunAsync(CancellationToken cancellationToken)
    {
        if (!context.IsSystem)
        {
            throw new InvalidOperationException("Saved-search alerts must run in an explicit system context.");
        }

        var settings = options.Value.Alerts;
        var now = clock.GetUtcNow();
        var dailyCutoff = now.AddHours(-settings.DailyEveryHours);
        var weeklyCutoff = now.AddDays(-settings.WeeklyEveryDays);
        var due = await db.SavedSearches
            .Where(s => (s.NotificationFrequency == NotificationFrequency.Daily && (s.LastAlertedAt == null || s.LastAlertedAt <= dailyCutoff))
                || (s.NotificationFrequency == NotificationFrequency.Weekly && (s.LastAlertedAt == null || s.LastAlertedAt <= weeklyCutoff)))
            .OrderBy(s => s.LastAlertedAt)
            .Take(settings.BatchSize)
            .ToListAsync(cancellationToken);

        int sent = 0, reported = 0;
        foreach (var search in due)
        {
            try
            {
                var since = search.LastAlertedAt ?? search.CreatedAt;
                var already = db.SavedSearchAlerts.Where(a => a.SavedSearchId == search.Id).Select(a => a.OpportunityId);
                var matches = await OpportunityFilter.Apply(db.Opportunities.AsNoTracking(), search.Criteria, now)
                    .Where(o => o.FirstIngestedAt > since && !already.Contains(o.Id))
                    .OrderByDescending(o => o.FirstIngestedAt)
                    .Select(o => new { o.Id, o.Title })
                    .Take(settings.MaxItemsPerAlert + 1)
                    .ToListAsync(cancellationToken);

                search.RecordAlertCheck(now);
                var listed = matches.Take(settings.MaxItemsPerAlert).ToList();
                foreach (var match in listed)
                {
                    db.SavedSearchAlerts.Add(new SavedSearchAlert(Guid.NewGuid(), search.TenantId, search.Id, match.Id, now));
                }

                await db.SaveChangesAsync(cancellationToken);
                if (listed.Count == 0)
                {
                    continue;
                }

                var more = matches.Count > listed.Count;
                var title = $"{listed.Count}{(more ? "+" : string.Empty)} new {(listed.Count == 1 && !more ? "opportunity matches" : "opportunities match")} “{search.Name}”";
                var body = string.Join("\n", listed.Take(5).Select(m => "• " + m.Title)) + (listed.Count > 5 || more ? "\n…and more" : string.Empty);
                await notifications.PublishAsync(search.TenantId, [search.CreatedByUserId], NotificationKinds.SavedSearchAlert, title, body, $"/Opportunities?savedSearch={search.Id}", cancellationToken);
                sent++;
                reported += listed.Count;
            }
#pragma warning disable CA1031 // One bad search must not stop alerts for every other company.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "Saved-search alert failed for search {SavedSearchId}.", search.Id);
            }
        }

        return new AlertRunSummary(due.Count, sent, reported);
    }
}
