using Microsoft.Extensions.Logging;

namespace GovBidPro.Application.Opportunities;

public sealed class OpportunitySyncOptions
{
    public const string SectionName = "OpportunitySources:Sync";

    /// <summary>Runs the scheduled sync of every enabled live source. Off by default.</summary>
    public bool Enabled { get; set; }

    public int IntervalMinutes { get; set; } = 360;

    /// <summary>Upper bound of source requests per source per run, to respect source quotas.</summary>
    public int MaxPagesPerRun { get; set; } = 5;
}

public sealed record SourceSyncResult(string SourceKey, ImportRunSummary Summary);

/// <summary>
/// Runs a bounded import for every registered scheduled source (handoff §6.2). Sources are registered
/// as <see cref="IOpportunitySource"/> only when enabled by configuration; the fixture source is not
/// scheduled. Must run in a system context because opportunity data is global.
/// </summary>
public sealed class OpportunitySyncService(IEnumerable<IOpportunitySource> sources, OpportunityImportService imports, ILogger<OpportunitySyncService> logger)
{
    public IReadOnlyList<string> SourceKeys { get; } = sources.Select(s => s.SourceKey).ToList();

    public async Task<IReadOnlyList<SourceSyncResult>> RunAsync(int maxPagesPerSource, CancellationToken cancellationToken)
    {
        var results = new List<SourceSyncResult>();
        foreach (var source in sources)
        {
            var summary = await imports.RunAsync(source, Math.Max(1, maxPagesPerSource), cancellationToken);
            logger.LogInformation(
                "Sync {SourceKey}: {Status}, pages {Pages}, created {Created}, amended {Amended}, unchanged {Unchanged}, rejected {Rejected}, error {ErrorCode}.",
                source.SourceKey, summary.Status, summary.Pages, summary.Created, summary.Amended, summary.Unchanged, summary.Rejected, summary.ErrorCode);
            results.Add(new SourceSyncResult(source.SourceKey, summary));
        }

        return results;
    }
}
