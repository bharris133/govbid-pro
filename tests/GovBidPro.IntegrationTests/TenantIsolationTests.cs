using GovBidPro.Application.Common;
using GovBidPro.Application.SavedSearches;
using GovBidPro.Application.Security;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using GovBidPro.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GovBidPro.IntegrationTests;

/// <summary>SEC-01: Tenant A cannot read, edit, list, download or delete Tenant B data via guessed IDs.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class TenantIsolationTests(PostgresFixture postgres)
{
    private async Task<(TestWorld World, Guid TenantA, string UserA, Guid TenantB, string UserB)> SetupAsync()
    {
        var world = new TestWorld(await postgres.CreateMigratedDatabaseAsync(), new MutableTimeProvider(DateTimeOffset.UtcNow));
        await world.SyncRetentionAsync();
        var (a, ua) = await world.CreateCompanyAsync("Tenant A");
        var (b, ub) = await world.CreateCompanyAsync("Tenant B");
        return (world, a, ua, b, ub);
    }

    [Fact]
    public async Task Saved_searches_are_invisible_and_immutable_across_tenants()
    {
        var (world, a, ua, b, ub) = await SetupAsync();
        var tenantA = TestWorld.Tenant(a, ua);
        var tenantB = TestWorld.Tenant(b, ub);
        SavedSearchDto created;
        await using (var db = world.Context(tenantA))
        {
            created = await world.SavedSearches(db, tenantA).CreateAsync(new CreateSavedSearchCommand("A search", new OpportunitySearchCriteria { Keywords = "cloud" }, NotificationFrequency.None), CancellationToken.None);
        }

        await using (var db = world.Context(tenantB))
        {
            var audit = new RecordingAuditLog();
            var service = world.SavedSearches(db, tenantB, audit);
            Assert.Empty(await service.ListAsync(CancellationToken.None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.GetAsync(created.Id, CancellationToken.None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.DeleteAsync(created.Id, CancellationToken.None));
            Assert.All(audit.Entries, e => Assert.Equal(AuditOutcome.Denied, e.Outcome));
            Assert.Equal(2, audit.Entries.Count);
        }

        await using (var db = world.Context(tenantA))
        {
            Assert.Single(await world.SavedSearches(db, tenantA).ListAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task Documents_cannot_be_downloaded_deleted_or_listed_across_tenants()
    {
        var (world, a, ua, b, ub) = await SetupAsync();
        var tenantA = TestWorld.Tenant(a, ua);
        var tenantB = TestWorld.Tenant(b, ub);
        Guid documentId;
        await using (var db = world.Context(tenantA))
        {
            documentId = (await world.Documents(db, tenantA).UploadAsync(TestWorld.TextUpload("Pricing notes"), CancellationToken.None)).Id;
        }

        await using (var db = world.Context(tenantB))
        {
            var service = world.Documents(db, tenantB);
            Assert.Empty(await service.ListAsync(CancellationToken.None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.OpenDownloadAsync(documentId, CancellationToken.None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.DeleteAsync(documentId, CancellationToken.None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.QuarantineAsync(documentId, CancellationToken.None));

            var policy = new DocumentDataPolicyEvaluator(db, Options.Create(new DataHandlingOptions()));
            foreach (var purpose in Enum.GetValues<ProcessingPurpose>())
            {
                var decision = await policy.EvaluateAsync(new DataAccessContext(b, ub, purpose), documentId, CancellationToken.None);
                Assert.False(decision.Allowed);
            }
        }

        await using (var db = world.Context(tenantA))
        {
            await using var stream = (await world.Documents(db, tenantA).OpenDownloadAsync(documentId, CancellationToken.None)).Content;
            Assert.True(stream.Length > 0);
        }
    }

    [Fact]
    public async Task Global_query_filter_fails_closed_without_a_tenant()
    {
        var (world, a, ua, _, _) = await SetupAsync();
        var tenantA = TestWorld.Tenant(a, ua);
        await using (var db = world.Context(tenantA))
        {
            await world.SavedSearches(db, tenantA).CreateAsync(new CreateSavedSearchCommand("A", new OpportunitySearchCriteria(), NotificationFrequency.None), CancellationToken.None);
        }

        await using (var anonymous = world.Context(new Application.Abstractions.TenantContextHolder()))
        {
            Assert.Equal(0, await anonymous.SavedSearches.CountAsync());
            Assert.Equal(0, await anonymous.CompanyMemberships.CountAsync());
        }

        await using (var system = world.Context(PostgresFixture.SystemContext()))
        {
            Assert.Equal(1, await system.SavedSearches.CountAsync());
        }
    }

    [Fact]
    public async Task Reviewer_role_cannot_create_saved_searches()
    {
        var (world, a, ua, _, _) = await SetupAsync();
        var reviewer = TestWorld.Tenant(a, ua, CompanyRole.Reviewer);
        await using var db = world.Context(reviewer);
        var ex = await Assert.ThrowsAsync<PolicyDeniedException>(() => world.SavedSearches(db, reviewer).CreateAsync(new CreateSavedSearchCommand("x", new OpportunitySearchCriteria(), NotificationFrequency.None), CancellationToken.None));
        Assert.Equal("role.insufficient", ex.ReasonCode);
    }
}
