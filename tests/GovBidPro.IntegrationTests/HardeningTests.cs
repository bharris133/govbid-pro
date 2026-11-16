using System.Diagnostics;
using System.Text;
using GovBidPro.Application.Auditing;
using GovBidPro.Application.Common;
using GovBidPro.Application.Dashboard;
using GovBidPro.Application.Opportunities;
using GovBidPro.Domain.Common;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using GovBidPro.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace GovBidPro.IntegrationTests;

/// <summary>P1.7 hardening: structural tenant guardrail, company audit export and a repeatable performance baseline.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class HardeningTests(PostgresFixture postgres, ITestOutputHelper output)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Every_tenant_owned_entity_has_a_fail_closed_query_filter()
    {
        await using var db = PostgresFixture.CreateContext(await postgres.CreateMigratedDatabaseAsync(), PostgresFixture.SystemContext());
        var owned = db.Model.GetEntityTypes().Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType)).ToList();
        Assert.True(owned.Count >= 15, "Expected the tenant-owned entities to be discovered.");
        var unfiltered = owned.Where(t => t.GetDeclaredQueryFilters().Count == 0).Select(t => t.ClrType.Name).ToList();
        Assert.True(unfiltered.Count == 0, $"Tenant-owned entities without a query filter: {string.Join(", ", unfiltered)}");
    }

    [Fact]
    public async Task Tenant_owned_rows_are_invisible_without_a_resolved_tenant()
    {
        var world = new TestWorld(await postgres.CreateMigratedDatabaseAsync(), new MutableTimeProvider(Start));
        var (a, ua) = await world.CreateCompanyAsync("Tenant A");
        var tenantA = TestWorld.Tenant(a, ua);
        await using (var db = world.Context(tenantA))
        {
            await world.Tasks(db, tenantA).CreateAsync(new Application.Collaboration.TaskCommand("Private task", null, null, null), CancellationToken.None);
        }

        await using var anonymous = world.Context(new Application.Abstractions.TenantContextHolder());
        Assert.Empty(await anonymous.Set<WorkTask>().ToListAsync());
        Assert.Empty(await anonymous.CompanyMemberships.ToListAsync());
    }

    [Fact]
    public async Task Audit_export_is_admin_only_limited_to_the_callers_company_and_formula_safe()
    {
        var world = new TestWorld(await postgres.CreateMigratedDatabaseAsync(), new MutableTimeProvider(Start));
        var (a, ua) = await world.CreateCompanyAsync("Tenant A");
        var (b, ub) = await world.CreateCompanyAsync("Tenant B");
        var writer = await world.AddMemberAsync(a, CompanyRole.ProposalWriter);
        await using (var system = world.Context(PostgresFixture.SystemContext()))
        {
            system.AuditEvents.AddRange(
                new AuditEvent(Guid.NewGuid(), a, ua, "proposal.created", "proposal", "=HYPERLINK(\"x\")", AuditOutcome.Success, "ok", "c-1", Start.AddDays(-1)),
                new AuditEvent(Guid.NewGuid(), a, null, "document.scanned", "document", "d-1", AuditOutcome.Success, "v1:Clean", "rescan-job", Start.AddDays(-2)),
                new AuditEvent(Guid.NewGuid(), a, ua, "old.event", "proposal", "old", AuditOutcome.Success, "ok", "c-0", Start.AddDays(-200)),
                new AuditEvent(Guid.NewGuid(), b, ub, "secret.b_event", "proposal", "b-1", AuditOutcome.Success, "ok", "c-2", Start.AddDays(-1)),
                new AuditEvent(Guid.NewGuid(), null, null, "platform.event", "platform", null, AuditOutcome.Success, "ok", "c-3", Start.AddDays(-1)));
            await system.SaveChangesAsync();
        }

        var admin = TestWorld.Tenant(a, ua);
        await using (var db = world.Context(admin))
        {
            var audit = new RecordingAuditLog();
            var service = new AuditExportService(db, admin, new DbUserDirectory(db), audit, world.Clock);
            var file = await service.ExportCsvAsync(null, null, CancellationToken.None);
            var text = Encoding.UTF8.GetString(file.Content);
            Assert.Equal("text/csv", file.ContentType);
            Assert.Contains("proposal.created", text, StringComparison.Ordinal);
            Assert.Contains("document.scanned", text, StringComparison.Ordinal);
            Assert.Contains(",system,", text, StringComparison.Ordinal);
            Assert.Contains("@example.test", text, StringComparison.Ordinal);
            Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("secret.b_event", text, StringComparison.Ordinal);
            Assert.DoesNotContain("platform.event", text, StringComparison.Ordinal);
            Assert.DoesNotContain("old.event", text, StringComparison.Ordinal);
            Assert.True(text.IndexOf("document.scanned", StringComparison.Ordinal) < text.IndexOf("proposal.created", StringComparison.Ordinal), "Oldest first.");
            var entry = Assert.Single(audit.Entries);
            Assert.Equal((AuditActions.AuditExported, "rows.2"), (entry.Action, entry.Reason));

            var wide = Encoding.UTF8.GetString((await service.ExportCsvAsync(Start.AddDays(-300), Start, CancellationToken.None)).Content);
            Assert.Contains("old.event", wide, StringComparison.Ordinal);
            await Assert.ThrowsAsync<RequestValidationException>(() => service.ExportCsvAsync(Start.AddDays(-400), Start, CancellationToken.None));
            await Assert.ThrowsAsync<RequestValidationException>(() => service.ExportCsvAsync(Start, Start.AddDays(-1), CancellationToken.None));
        }

        var nonAdmin = TestWorld.Tenant(a, writer, CompanyRole.ProposalWriter);
        await using (var db = world.Context(nonAdmin))
        {
            var service = new AuditExportService(db, nonAdmin, new DbUserDirectory(db), new RecordingAuditLog(), world.Clock);
            await Assert.ThrowsAsync<PolicyDeniedException>(() => service.ExportCsvAsync(null, null, CancellationToken.None));
        }
    }

    /// <summary>
    /// Baseline, not a load test: 5,000 notices, one user, local database. The budget is deliberately loose so it
    /// only fails on an order-of-magnitude regression (for example a lost index or an N+1 query). Measured numbers
    /// are written to the test output and recorded in docs/operations/performance-baseline.md.
    /// </summary>
    [Fact]
    public async Task Search_and_dashboard_stay_within_the_baseline_budget_at_five_thousand_notices()
    {
        const int Notices = 5000;
        var budget = TimeSpan.FromSeconds(2);
        var world = new TestWorld(await postgres.CreateMigratedDatabaseAsync(), new MutableTimeProvider(Start));
        var (a, ua) = await world.CreateCompanyAsync("Tenant A");
        string[] naics = ["541511", "541512", "561210", "236220", "541330"];
        string[] topics = ["cloud hosting", "facilities maintenance", "cybersecurity assessment", "construction services", "engineering support"];
        var seedTimer = Stopwatch.StartNew();
        await world.ImportAsync(Enumerable.Range(0, Notices)
            .Select(i => world.Notice($"perf-{i}", $"Request for Proposal: {topics[i % topics.Length]} {i}", dueInDays: (i % 60) - 5, naics: naics[i % naics.Length], description: $"Statement of work for {topics[i % topics.Length]} at site {i}."))
            .ToArray());
        output.WriteLine($"import of {Notices} notices: {seedTimer.ElapsedMilliseconds} ms");

        var tenant = TestWorld.Tenant(a, ua);
        await using var db = world.Context(tenant);
        var search = new OpportunityQueryService(db, tenant, world.Clock);
        var dashboard = new DashboardService(db, tenant, world.Clock);

        var measurements = new Dictionary<string, TimeSpan>
        {
            ["search, no filters, page 1"] = await MedianAsync(() => search.SearchAsync(new OpportunitySearchCriteria(), 1, 25, CancellationToken.None)),
            ["search, keyword"] = await MedianAsync(() => search.SearchAsync(new OpportunitySearchCriteria { Keywords = "cybersecurity" }, 1, 25, CancellationToken.None)),
            ["search, keyword, due soonest, page 20"] = await MedianAsync(() => search.SearchAsync(new OpportunitySearchCriteria { Keywords = "cloud" }, 20, 25, CancellationToken.None, OpportunitySort.DueSoonest)),
            ["dashboard"] = await MedianAsync(() => dashboard.GetAsync(CancellationToken.None)),
        };

        foreach (var (name, elapsed) in measurements)
        {
            output.WriteLine($"{name}: {elapsed.TotalMilliseconds:F0} ms (median of 5)");
        }

        Assert.Equal(Notices, (await search.SearchAsync(new OpportunitySearchCriteria(), 1, 1, CancellationToken.None)).Results.TotalCount);
        Assert.All(measurements, m => Assert.True(m.Value < budget, $"{m.Key} took {m.Value.TotalMilliseconds:F0} ms; budget is {budget.TotalMilliseconds:F0} ms."));
    }

    private static async Task<TimeSpan> MedianAsync<T>(Func<Task<T>> work)
    {
        await work(); // warm-up: query compilation and connection
        var samples = new List<TimeSpan>();
        for (var i = 0; i < 5; i++)
        {
            var timer = Stopwatch.StartNew();
            await work();
            samples.Add(timer.Elapsed);
        }

        samples.Sort();
        return samples[2];
    }
}
