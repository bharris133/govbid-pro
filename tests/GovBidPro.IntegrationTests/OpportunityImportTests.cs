using GovBidPro.Application.Opportunities;
using GovBidPro.Domain.Enums;
using GovBidPro.Infrastructure.SamGov;
using GovBidPro.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GovBidPro.IntegrationTests;

/// <summary>Handoff §6.2 ingestion acceptance: duplicates, amendments, missing fields, rate limits, interrupted pagination.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class OpportunityImportTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private sealed class ScriptedSource(string key, params Func<OpportunityImportPage>[] pages) : IOpportunitySource
    {
        public string SourceKey => key;

        public bool IsLive => false;

        public List<string?> CursorsSeen { get; } = [];

        public Task<OpportunityImportPage> FetchAsync(OpportunityImportCursor? cursor, CancellationToken cancellationToken)
        {
            CursorsSeen.Add(cursor?.Value);
            var index = cursor?.Value is null ? 0 : int.Parse(cursor.Value, System.Globalization.CultureInfo.InvariantCulture);
            return Task.FromResult(pages[index]());
        }
    }

    private static ImportedOpportunity Notice(string id, string? title = "Request for Proposal: Test", SourceNoticeType type = SourceNoticeType.Solicitation) => new()
    {
        ExternalId = id,
        Title = title,
        NoticeType = type,
        SourceNoticeTypeRaw = type.ToString(),
        NaicsCode = "541511",
        PostedAt = Start,
        ResponseDueAt = Start.AddDays(20),
        ResponseDueOriginalText = "December 3, 2026 4:00 PM ET",
        ResponseDueTimeZone = "America/New_York",
    };

    private static OpportunityImportPage Page(OpportunityImportCursor? next, params ImportedOpportunity[] items) => new(items, next);

    private async Task<(OpportunityImportService Service, Infrastructure.Persistence.GovBidProDbContext Db, MutableTimeProvider Clock)> CreateAsync()
    {
        var cs = await postgres.CreateMigratedDatabaseAsync();
        var clock = new MutableTimeProvider(Start);
        var db = PostgresFixture.CreateContext(cs, PostgresFixture.SystemContext());
        return (new OpportunityImportService(db, clock, NullLogger<OpportunityImportService>.Instance), db, clock);
    }

    [Fact]
    public async Task Import_is_idempotent_and_deduplicates_external_ids()
    {
        var (service, db, _) = await CreateAsync();
        await using var _db = db;
        var source = new ScriptedSource("test", () => Page(null, Notice("A"), Notice("B"), Notice("A")));

        var first = await service.RunAsync(source, 5, CancellationToken.None);
        Assert.Equal(ImportRunStatus.Succeeded, first.Status);
        Assert.Equal(2, first.Created);
        Assert.Equal(1, first.Unchanged);

        var second = await service.RunAsync(source, 5, CancellationToken.None);
        Assert.Equal(0, second.Created);
        Assert.Equal(3, second.Unchanged);
        Assert.Equal(2, await db.Opportunities.CountAsync());
        Assert.Equal(2, await db.OpportunityRevisions.CountAsync());
    }

    [Fact]
    public async Task Amended_notices_create_revisions_instead_of_overwriting_history()
    {
        var (service, db, _) = await CreateAsync();
        await using var _db = db;
        var title = "Request for Proposal: Original";
        var source = new ScriptedSource("test", () => Page(null, Notice("A", title)));
        await service.RunAsync(source, 5, CancellationToken.None);

        title = "Request for Proposal: Amendment 1";
        var summary = await service.RunAsync(source, 5, CancellationToken.None);
        Assert.Equal(1, summary.Amended);

        var opportunity = await db.Opportunities.AsNoTracking().SingleAsync();
        Assert.Equal(2, opportunity.RevisionNumber);
        Assert.Equal(title, opportunity.Title);
        var revisions = await db.OpportunityRevisions.AsNoTracking().OrderBy(r => r.RevisionNumber).ToListAsync();
        Assert.Equal([RevisionChangeKind.Created, RevisionChangeKind.Amended], revisions.Select(r => r.ChangeKind));
        Assert.Contains("Original", revisions[0].SnapshotJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_required_fields_are_rejected_without_failing_the_run()
    {
        var (service, db, _) = await CreateAsync();
        await using var _db = db;
        var source = new ScriptedSource("test", () => Page(null, Notice("A"), Notice("B", title: "  "), Notice(" ", title: "No id")));
        var summary = await service.RunAsync(source, 5, CancellationToken.None);
        Assert.Equal(ImportRunStatus.Succeeded, summary.Status);
        Assert.Equal(1, summary.Created);
        Assert.Equal(2, summary.Rejected);
    }

    [Fact]
    public async Task Interrupted_pagination_resumes_from_last_persisted_page()
    {
        var (service, db, clock) = await CreateAsync();
        await using var _db = db;
        var failSecondPage = true;
        var source = new ScriptedSource(
            "test",
            () => Page(new OpportunityImportCursor("1"), Notice("A")),
            () => failSecondPage ? throw new OpportunitySourceException("boom") : Page(null, Notice("B")));

        var first = await service.RunAsync(source, 5, CancellationToken.None);
        Assert.Equal(ImportRunStatus.PartiallySucceeded, first.Status);
        var state = await db.SourceSyncStates.AsNoTracking().SingleAsync();
        Assert.Equal("1", state.Cursor);
        Assert.Equal("source_error", state.LastErrorCode);
        Assert.Null(state.LastSuccessfulSyncAt);

        // Backoff prevents an immediate retry.
        Assert.Equal("backoff_active", (await service.RunAsync(source, 5, CancellationToken.None)).ErrorCode);

        clock.Advance(TimeSpan.FromHours(2));
        failSecondPage = false;
        source.CursorsSeen.Clear();
        var resumed = await service.RunAsync(source, 5, CancellationToken.None);
        Assert.Equal(ImportRunStatus.Succeeded, resumed.Status);
        Assert.Equal(["1"], source.CursorsSeen);
        Assert.Equal(2, await db.Opportunities.CountAsync());
        var healthy = await db.SourceSyncStates.AsNoTracking().SingleAsync();
        Assert.Null(healthy.Cursor);
        Assert.NotNull(healthy.LastSuccessfulSyncAt);
        Assert.Equal(0, healthy.ConsecutiveFailures);
    }

    [Fact]
    public async Task Rate_limit_stops_the_run_and_records_retry_time()
    {
        var (service, db, clock) = await CreateAsync();
        await using var _db = db;
        var source = new ScriptedSource("test", () => throw new OpportunitySourceRateLimitedException(TimeSpan.FromMinutes(10)));
        var summary = await service.RunAsync(source, 5, CancellationToken.None);
        Assert.Equal(ImportRunStatus.RateLimited, summary.Status);
        var state = await db.SourceSyncStates.AsNoTracking().SingleAsync();
        Assert.Equal(clock.GetUtcNow().AddMinutes(10), state.RetryNotBefore);
    }

    [Fact]
    public async Task Fixture_source_is_stored_as_non_live_data()
    {
        var (service, db, _) = await CreateAsync();
        await using var _db = db;
        var summary = await service.RunAsync(new FixtureOpportunitySource(), 20, CancellationToken.None);
        Assert.Equal(12, summary.Created);
        Assert.False(await db.Opportunities.AnyAsync(o => o.IsLiveSource));
        Assert.True(await db.Opportunities.AllAsync(o => o.Title.StartsWith("[FIXTURE]")));
        Assert.True(await db.Opportunities.AllAsync(o => o.SourceUrl!.StartsWith("https://example.invalid/")));
        var due = await db.Opportunities.AsNoTracking().FirstAsync(o => o.ExternalId == "FX-0001");
        Assert.Equal("America/New_York", due.Deadline.SourceTimeZone);
        Assert.NotNull(due.Deadline.OriginalText);
        Assert.Equal(ResponseType.Rfp, due.ResponseType);
        Assert.Equal(SourceNoticeType.Solicitation, due.NoticeType);
    }
}
