using GovBidPro.Application.Assessments;
using GovBidPro.Application.Auditing;
using GovBidPro.Application.Common;
using GovBidPro.Application.Dashboard;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using GovBidPro.Testing;
using Microsoft.EntityFrameworkCore;

namespace GovBidPro.IntegrationTests;

/// <summary>P1.3: company profile, scored assessments, Go/No-go roles, append-only history and tenant isolation.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class AssessmentTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private async Task<(TestWorld World, Guid Tenant, string Admin, Guid Opportunity)> SetupAsync()
    {
        var world = new TestWorld(await postgres.CreateMigratedDatabaseAsync(), new MutableTimeProvider(Start));
        var (tenant, admin) = await world.CreateCompanyAsync("Tenant A");
        await world.ImportAsync(world.Notice("N1", "Request for Proposal: Cloud migration", dueInDays: 20, setAside: "Total Small Business Set-Aside"));
        return (world, tenant, admin, await world.OpportunityIdAsync("N1"));
    }

    private static CompanyFitProfile Profile() => new() { NaicsCodes = ["541511"], SetAsideQualifications = [SetAsideCategory.SmallBusiness], CapabilityKeywords = ["cloud", "migration"], PreferredStates = ["VA"] };

    [Fact]
    public async Task Profile_edits_need_a_manager_and_drive_the_score()
    {
        var (world, tenant, admin, opportunity) = await SetupAsync();
        var writerId = await world.AddMemberAsync(tenant, CompanyRole.ProposalWriter);
        var writer = TestWorld.Tenant(tenant, writerId, CompanyRole.ProposalWriter);
        await using (var db = world.Context(writer))
        {
            var service = world.Assessments(db, writer);
            Assert.True((await service.GetProfileAsync(CancellationToken.None)).IsEmpty);
            Assert.NotEqual(100, (await service.PreviewAsync(opportunity, CancellationToken.None)).Score);
            await Assert.ThrowsAsync<PolicyDeniedException>(() => service.UpdateProfileAsync(Profile(), CancellationToken.None));
        }

        var manager = TestWorld.Tenant(tenant, admin, CompanyRole.TeamManager);
        await using (var db = world.Context(manager))
        {
            var service = world.Assessments(db, manager);
            var saved = await service.UpdateProfileAsync(Profile(), CancellationToken.None);
            Assert.False(saved.IsEmpty);
            Assert.EndsWith("@example.test", saved.UpdatedBy, StringComparison.Ordinal);
            await Assert.ThrowsAsync<RequestValidationException>(() => service.UpdateProfileAsync(new CompanyFitProfile { NaicsCodes = ["x"] }, CancellationToken.None));

            var fit = await service.PreviewAsync(opportunity, CancellationToken.None);
            Assert.Equal(100, fit.Score);
            Assert.Equal(0, await db.OpportunityAssessments.CountAsync());
        }
    }

    [Fact]
    public async Task Decisions_are_role_controlled_audited_and_append_only()
    {
        var (world, tenant, admin, opportunity) = await SetupAsync();
        var manager = TestWorld.Tenant(tenant, admin, CompanyRole.TeamManager);
        await using (var db = world.Context(manager))
        {
            await world.Assessments(db, manager).UpdateProfileAsync(Profile(), CancellationToken.None);
        }

        var writerId = await world.AddMemberAsync(tenant, CompanyRole.ProposalWriter);
        var writer = TestWorld.Tenant(tenant, writerId, CompanyRole.ProposalWriter);
        var audit = new RecordingAuditLog();
        await using (var db = world.Context(writer))
        {
            var service = world.Assessments(db, writer, audit);
            var denied = await Assert.ThrowsAsync<PolicyDeniedException>(() => service.RecordAsync(opportunity, new RecordAssessmentCommand(GoNoGoDecision.Go, "We should bid."), CancellationToken.None));
            Assert.Equal("role.insufficient", denied.ReasonCode);
            var watch = await service.RecordAsync(opportunity, new RecordAssessmentCommand(GoNoGoDecision.Watch, null), CancellationToken.None);
            Assert.Equal(1, watch.Sequence);
            Assert.Equal(100, watch.Score);
        }

        var reviewer = TestWorld.Tenant(tenant, admin, CompanyRole.Reviewer);
        await using (var db = world.Context(reviewer))
        {
            await Assert.ThrowsAsync<PolicyDeniedException>(() => world.Assessments(db, reviewer).RecordAsync(opportunity, new RecordAssessmentCommand(GoNoGoDecision.Watch, null), CancellationToken.None));
        }

        world.Clock.Advance(TimeSpan.FromDays(1));
        await using (var db = world.Context(manager))
        {
            var service = world.Assessments(db, manager, audit);
            await Assert.ThrowsAsync<RequestValidationException>(() => service.RecordAsync(opportunity, new RecordAssessmentCommand(GoNoGoDecision.Go, " "), CancellationToken.None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.RecordAsync(Guid.NewGuid(), new RecordAssessmentCommand(GoNoGoDecision.Watch, null), CancellationToken.None));
            var go = await service.RecordAsync(opportunity, new RecordAssessmentCommand(GoNoGoDecision.Go, "Core capability and strong past performance."), CancellationToken.None);
            Assert.Equal(2, go.Sequence);
            Assert.Equal(6, go.Criteria.Count);

            var history = await service.HistoryAsync(opportunity, CancellationToken.None);
            Assert.Equal([GoNoGoDecision.Go, GoNoGoDecision.Watch], history.Select(h => h.Decision));
            Assert.All(history, h => Assert.EndsWith("@example.test", h.CreatedBy, StringComparison.Ordinal));

            var latest = Assert.Single(await service.ListLatestAsync(null, CancellationToken.None));
            Assert.Equal(GoNoGoDecision.Go, latest.Decision);
            Assert.False(latest.OpportunityAmendedSince);
            Assert.Empty(await service.ListLatestAsync(GoNoGoDecision.Watch, CancellationToken.None));

            var dashboard = await new DashboardService(db, manager, world.Clock).GetAsync(CancellationToken.None);
            Assert.Equal(new DecisionCountsDto(Go: 1, NoGo: 0, Watch: 0, Undecided: 0), dashboard.Decisions);
        }

        Assert.Contains(audit.Entries, e => e.Action == AuditActions.AssessmentRecorded && e.Outcome == AuditOutcome.Denied);
        Assert.Contains(audit.Entries, e => e.Action == AuditActions.AssessmentRecorded && e.Outcome == AuditOutcome.Success && e.Reason == "decision.Go");

        // An amended notice is flagged against the earlier decision instead of silently changing it.
        await world.ImportAsync(world.Notice("N1", "Request for Proposal: Cloud migration (Amendment 1)", dueInDays: 25, setAside: "Total Small Business Set-Aside"));
        await using (var db = world.Context(manager))
        {
            var latest = Assert.Single(await world.Assessments(db, manager).ListLatestAsync(GoNoGoDecision.Go, CancellationToken.None));
            Assert.True(latest.OpportunityAmendedSince);
            Assert.Equal(100, latest.Score);
        }
    }

    [Fact]
    public async Task Assessments_and_profiles_are_isolated_between_companies()
    {
        var (world, tenantA, adminA, opportunity) = await SetupAsync();
        var (tenantB, adminB) = await world.CreateCompanyAsync("Tenant B");
        var a = TestWorld.Tenant(tenantA, adminA);
        var b = TestWorld.Tenant(tenantB, adminB);
        await using (var db = world.Context(a))
        {
            var service = world.Assessments(db, a);
            await service.UpdateProfileAsync(Profile(), CancellationToken.None);
            await service.RecordAsync(opportunity, new RecordAssessmentCommand(GoNoGoDecision.NoGo, "Incumbent is entrenched."), CancellationToken.None);
        }

        await using (var db = world.Context(b))
        {
            var service = world.Assessments(db, b);
            Assert.True((await service.GetProfileAsync(CancellationToken.None)).IsEmpty);
            Assert.Empty(await service.HistoryAsync(opportunity, CancellationToken.None));
            Assert.Empty(await service.ListLatestAsync(null, CancellationToken.None));

            // B scores the same public notice against its own (empty) profile and gets its own sequence.
            var own = await service.RecordAsync(opportunity, new RecordAssessmentCommand(GoNoGoDecision.Watch, null), CancellationToken.None);
            Assert.Equal(1, own.Sequence);
            Assert.NotEqual(100, own.Score);
        }

        await using (var anonymous = world.Context(new Application.Abstractions.TenantContextHolder()))
        {
            Assert.Equal(0, await anonymous.OpportunityAssessments.CountAsync());
            Assert.Equal(0, await anonymous.CompanyProfiles.CountAsync());
        }
    }
}
