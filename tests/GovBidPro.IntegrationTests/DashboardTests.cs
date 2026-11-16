using GovBidPro.Application.Dashboard;
using GovBidPro.Application.Opportunities;
using GovBidPro.Application.SavedSearches;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using GovBidPro.Testing;

namespace GovBidPro.IntegrationTests;

/// <summary>P1.1 discovery dashboard: counts, deadlines, saved-search summaries and tenant isolation.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class DashboardTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Dashboard_summarizes_open_due_soon_and_saved_search_matches_for_the_callers_company_only()
    {
        var world = new TestWorld(await postgres.CreateMigratedDatabaseAsync(), new MutableTimeProvider(Start));
        var (a, ua) = await world.CreateCompanyAsync("Tenant A");
        var (b, ub) = await world.CreateCompanyAsync("Tenant B");
        await world.ImportAsync(
            world.Notice("soon", "Request for Proposal: Cloud hosting", dueInDays: 3),
            world.Notice("later", "RFQ: Cloud licenses", dueInDays: 30, naics: "541519"),
            world.Notice("closed", "Request for Proposal: Closed cloud work", dueInDays: -2),
            world.Notice("award", "Award notice", dueInDays: 5, type: SourceNoticeType.AwardNotice));

        var tenantA = TestWorld.Tenant(a, ua);
        var tenantB = TestWorld.Tenant(b, ub);
        await using (var db = world.Context(tenantA))
        {
            await world.SavedSearches(db, tenantA).CreateAsync(new CreateSavedSearchCommand("Cloud", new OpportunitySearchCriteria { Keywords = "cloud" }, NotificationFrequency.Daily), CancellationToken.None);
        }

        await using (var db = world.Context(tenantB))
        {
            await world.SavedSearches(db, tenantB).CreateAsync(new CreateSavedSearchCommand("B only", new OpportunitySearchCriteria(), NotificationFrequency.None), CancellationToken.None);
        }

        await using (var db = world.Context(tenantA))
        {
            var dashboard = await new DashboardService(db, tenantA, world.Clock).GetAsync(CancellationToken.None);
            Assert.Equal(new DiscoverySummaryDto(OpenOpportunities: 2, DueWithin7Days: 1, NewLast7Days: 4, AmendedLast7Days: 0), dashboard.Discovery);
            Assert.Equal(["Request for Proposal: Cloud hosting", "RFQ: Cloud licenses"], dashboard.UpcomingDeadlines.Select(d => d.Title));
            var search = Assert.Single(dashboard.SavedSearches);
            Assert.Equal("Cloud", search.Name);
            Assert.Equal(2, search.OpenMatches);
            Assert.Equal(2, search.NewMatchesLast7Days);
            Assert.False(Assert.Single(dashboard.Sources).IsLiveSource);
        }
    }

    [Fact]
    public async Task Search_can_sort_by_soonest_deadline_and_saved_searches_can_be_updated()
    {
        var world = new TestWorld(await postgres.CreateMigratedDatabaseAsync(), new MutableTimeProvider(Start));
        var (a, ua) = await world.CreateCompanyAsync("Tenant A");
        await world.ImportAsync(world.Notice("x", dueInDays: 30), world.Notice("y", dueInDays: 2), world.Notice("z", dueInDays: 10));
        var tenant = TestWorld.Tenant(a, ua);
        await using var db = world.Context(tenant);

        var result = await new OpportunityQueryService(db, tenant, world.Clock).SearchAsync(new OpportunitySearchCriteria { OpenOnly = true }, 1, 10, CancellationToken.None, OpportunitySort.DueSoonest);
        Assert.Equal([2, 10, 30], result.Results.Items.Select(i => (int)Math.Round((i.ResponseDueAt!.Value - Start).TotalDays)));

        var searches = world.SavedSearches(db, tenant);
        var created = await searches.CreateAsync(new CreateSavedSearchCommand("First", new OpportunitySearchCriteria(), NotificationFrequency.None), CancellationToken.None);
        var updated = await searches.UpdateAsync(created.Id, new CreateSavedSearchCommand("Renamed", new OpportunitySearchCriteria { NaicsCodes = ["541511"] }, NotificationFrequency.Weekly), CancellationToken.None);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal(NotificationFrequency.Weekly, updated.NotificationFrequency);
        Assert.Equal(["541511"], (await searches.GetAsync(created.Id, CancellationToken.None)).Criteria.NaicsCodes);
    }
}
