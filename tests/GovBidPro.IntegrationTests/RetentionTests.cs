using GovBidPro.Application.Retention;
using GovBidPro.Domain.Enums;
using GovBidPro.Testing;
using Microsoft.EntityFrameworkCore;

namespace GovBidPro.IntegrationTests;

/// <summary>RET-01/RET-02/SEC-06: configurable, versioned retention with idempotent purge and legal hold.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class RetentionTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Recovery_window_comes_from_configuration_and_purge_is_idempotent()
    {
        var world = new TestWorld(await postgres.CreateMigratedDatabaseAsync(), new MutableTimeProvider(new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero)));
        var policies = new Dictionary<string, RetentionPeriodOptions>(TestWorld.DefaultRetention.Policies, StringComparer.Ordinal)
        {
            [RetentionPolicyKeys.DeletedDocumentRecovery] = new() { Days = 7 },
        };
        world.Retention = new RetentionOptions { ApprovalStatus = TestWorld.DefaultRetention.ApprovalStatus, Policies = policies, PurgeJob = TestWorld.DefaultRetention.PurgeJob };
        await world.SyncRetentionAsync();
        var (tenantId, userId) = await world.CreateCompanyAsync("Retention Co");
        var tenant = TestWorld.Tenant(tenantId, userId);

        Guid id;
        await using (var db = world.Context(tenant))
        {
            var service = world.Documents(db, tenant);
            id = (await service.UploadAsync(TestWorld.TextUpload("to delete"), CancellationToken.None)).Id;
            var deleted = await service.DeleteAsync(id, CancellationToken.None);
            Assert.Equal(world.Clock.GetUtcNow().AddDays(7), deleted.RecoverableUntil);
        }

        world.Clock.Advance(TimeSpan.FromDays(6));
        await using (var db = world.Context(PostgresFixture.SystemContext()))
        {
            Assert.Equal(0, (await world.Purge(db, PostgresFixture.SystemContext()).RunOnceAsync(CancellationToken.None)).Purged);
        }

        Assert.Single(world.Storage.Objects);
        world.Clock.Advance(TimeSpan.FromDays(2));
        await using (var db = world.Context(PostgresFixture.SystemContext()))
        {
            var system = PostgresFixture.SystemContext();
            Assert.Equal(1, (await world.Purge(db, system).RunOnceAsync(CancellationToken.None)).Purged);
            Assert.Equal(0, (await world.Purge(db, system).RunOnceAsync(CancellationToken.None)).Purged);
            Assert.Empty(world.Storage.Objects);
            var doc = await db.StoredDocuments.AsNoTracking().SingleAsync(d => d.Id == id);
            Assert.Equal(DocumentState.Purged, doc.State);
            var action = await db.RetentionActions.AsNoTracking().SingleAsync(a => a.ResourceId == id);
            Assert.Equal(RetentionActionStatus.Completed, action.Status);
            Assert.Equal(7, (await db.RetentionPolicies.AsNoTracking().SingleAsync(p => p.Id == action.PolicyId)).RetentionDays);
            Assert.True(await db.AuditEvents.AnyAsync(e => e.Action == "document.purge" && e.ResourceId == id.ToString()));
        }
    }

    [Fact]
    public async Task Restore_cancels_purge_and_legal_hold_suspends_it()
    {
        var world = new TestWorld(await postgres.CreateMigratedDatabaseAsync(), new MutableTimeProvider(DateTimeOffset.UtcNow));
        await world.SyncRetentionAsync();
        var (tenantId, userId) = await world.CreateCompanyAsync("Hold Co");
        var tenant = TestWorld.Tenant(tenantId, userId);

        Guid restored, held;
        await using (var db = world.Context(tenant))
        {
            var service = world.Documents(db, tenant);
            restored = (await service.UploadAsync(TestWorld.TextUpload("restore me"), CancellationToken.None)).Id;
            held = (await service.UploadAsync(TestWorld.TextUpload("hold me"), CancellationToken.None)).Id;
            await service.DeleteAsync(restored, CancellationToken.None);
            await service.DeleteAsync(held, CancellationToken.None);
            await service.RestoreAsync(restored, CancellationToken.None);
            var doc = await db.StoredDocuments.SingleAsync(d => d.Id == held);
            doc.SetLegalHold(true);
            await db.SaveChangesAsync();
        }

        world.Clock.Advance(TimeSpan.FromDays(31));
        await using (var db = world.Context(PostgresFixture.SystemContext()))
        {
            var summary = await world.Purge(db, PostgresFixture.SystemContext()).RunOnceAsync(CancellationToken.None);
            Assert.Equal(0, summary.Purged);
            Assert.Equal(1, summary.SkippedForLegalHold);
            Assert.Equal(2, world.Storage.Objects.Count);
            var cancelled = await db.RetentionActions.AsNoTracking().SingleAsync(a => a.ResourceId == restored);
            Assert.Equal(RetentionActionStatus.Cancelled, cancelled.Status);
        }
    }

    [Fact]
    public async Task Changing_configuration_creates_a_new_policy_version()
    {
        var world = new TestWorld(await postgres.CreateMigratedDatabaseAsync(), new MutableTimeProvider(DateTimeOffset.UtcNow));
        await world.SyncRetentionAsync();
        await world.SyncRetentionAsync();
        var policies = new Dictionary<string, RetentionPeriodOptions>(TestWorld.DefaultRetention.Policies, StringComparer.Ordinal)
        {
            [RetentionPolicyKeys.SecurityAuditEvents] = new() { Days = 400 },
        };
        world.Retention = new RetentionOptions { ApprovalStatus = TestWorld.DefaultRetention.ApprovalStatus, Policies = policies };
        await world.SyncRetentionAsync();

        await using var db = world.Context(PostgresFixture.SystemContext());
        var versions = await db.RetentionPolicies.AsNoTracking().Where(p => p.PolicyKey == RetentionPolicyKeys.SecurityAuditEvents).OrderBy(p => p.Version).ToListAsync();
        Assert.Equal(2, versions.Count);
        Assert.NotNull(versions[0].SupersededAt);
        Assert.Equal(400, versions[1].RetentionDays);
        Assert.Equal(RetentionPolicyKeys.Required.Count + 1, await db.RetentionPolicies.CountAsync());
    }

    [Fact]
    public async Task Purge_refuses_to_run_without_explicit_system_context()
    {
        var world = new TestWorld(await postgres.CreateMigratedDatabaseAsync(), new MutableTimeProvider(DateTimeOffset.UtcNow));
        var tenant = TestWorld.Tenant(Guid.NewGuid(), "user");
        await using var db = world.Context(tenant);
        await Assert.ThrowsAsync<InvalidOperationException>(() => world.Purge(db, tenant).RunOnceAsync(CancellationToken.None));
    }
}
