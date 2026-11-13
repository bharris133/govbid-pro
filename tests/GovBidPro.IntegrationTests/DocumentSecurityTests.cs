using GovBidPro.Application.Common;
using GovBidPro.Domain.Enums;
using GovBidPro.Testing;
using Microsoft.EntityFrameworkCore;

namespace GovBidPro.IntegrationTests;

/// <summary>SEC-02/SEC-03: restricted content never reaches ordinary storage; downloads deny by default.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class DocumentSecurityTests(PostgresFixture postgres)
{
    private async Task<(TestWorld World, Application.Abstractions.TenantContextHolder Tenant)> SetupAsync()
    {
        var world = new TestWorld(await postgres.CreateMigratedDatabaseAsync(), new MutableTimeProvider(DateTimeOffset.UtcNow));
        await world.SyncRetentionAsync();
        var (tenantId, userId) = await world.CreateCompanyAsync("Uploader Co");
        return (world, TestWorld.Tenant(tenantId, userId));
    }

    public static TheoryData<string, string> RejectedUploads() => new()
    {
        { "CUI//SP-PRVCY\nSample", "upload.suspected_restricted_content.marker.cui" },
        { "SECRET//NOFORN\nSample", "upload.suspected_restricted_content.marker.classified" },
        { "FOR OFFICIAL USE ONLY\nSample", "upload.suspected_restricted_content.marker.fouo" },
    };

    [Theory]
    [MemberData(nameof(RejectedUploads))]
    public async Task Marked_content_is_rejected_before_storage(string text, string reason)
    {
        var (world, tenant) = await SetupAsync();
        await using var db = world.Context(tenant);
        var audit = new RecordingAuditLog();
        var ex = await Assert.ThrowsAsync<PolicyDeniedException>(() => world.Documents(db, tenant, audit).UploadAsync(TestWorld.TextUpload(text), CancellationToken.None));
        Assert.Equal(reason, ex.ReasonCode);
        Assert.Empty(world.Storage.Objects);
        Assert.Equal(0, await db.StoredDocuments.CountAsync());
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.DoesNotContain("Sample", entry.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DataClassification.RestrictedPendingReview, true, "upload.declared_restricted_or_prohibited")]
    [InlineData(DataClassification.ProhibitedClassified, true, "upload.declared_restricted_or_prohibited")]
    [InlineData(DataClassification.Public, true, "upload.public_classification_not_allowed")]
    [InlineData(DataClassification.ConfidentialCommercial, false, "upload.declaration_missing")]
    public async Task Declarations_are_enforced_before_reading_content(DataClassification classification, bool affirm, string reason)
    {
        var (world, tenant) = await SetupAsync();
        await using var db = world.Context(tenant);
        var scanner = new FixedMalwareScanner(ScanStatus.Clean);
        world.Scanner = scanner;
        var ex = await Assert.ThrowsAsync<PolicyDeniedException>(() => world.Documents(db, tenant).UploadAsync(TestWorld.TextUpload("ok", classification, affirm), CancellationToken.None));
        Assert.Equal(reason, ex.ReasonCode);
        Assert.Equal(0, scanner.Calls);
        Assert.Empty(world.Storage.Objects);
    }

    [Fact]
    public async Task Spoofed_file_types_and_oversized_files_are_rejected()
    {
        var (world, tenant) = await SetupAsync();
        world.Uploads.MaxUploadBytes = 16;
        await using var db = world.Context(tenant);
        var service = world.Documents(db, tenant);
        var spoof = await Assert.ThrowsAsync<PolicyDeniedException>(() => service.UploadAsync(TestWorld.TextUpload("not a pdf", fileName: "x.pdf", contentType: "application/pdf"), CancellationToken.None));
        Assert.Equal("upload.file_type_not_allowed", spoof.ReasonCode);

        var big = TestWorld.TextUpload(new string('a', 64));
        var lying = big with { DeclaredLength = 4 };
        var oversized = await Assert.ThrowsAsync<PolicyDeniedException>(() => service.UploadAsync(lying, CancellationToken.None));
        Assert.Equal("upload.size_not_allowed", oversized.ReasonCode);
        Assert.Empty(world.Storage.Objects);
    }

    [Fact]
    public async Task Infected_uploads_are_rejected_and_unscanned_documents_cannot_be_downloaded()
    {
        var (world, tenant) = await SetupAsync();
        world.Scanner = new FixedMalwareScanner(ScanStatus.Infected);
        await using (var db = world.Context(tenant))
        {
            var ex = await Assert.ThrowsAsync<PolicyDeniedException>(() => world.Documents(db, tenant).UploadAsync(TestWorld.TextUpload("eicar-like"), CancellationToken.None));
            Assert.Equal("upload.malware_detected", ex.ReasonCode);
            Assert.Empty(world.Storage.Objects);
        }

        world.Scanner = new FixedMalwareScanner(ScanStatus.ScannerNotConfigured);
        await using (var db = world.Context(tenant))
        {
            var service = world.Documents(db, tenant);
            var doc = await service.UploadAsync(TestWorld.TextUpload("fine"), CancellationToken.None);
            Assert.False(doc.IsDownloadable);
            var denied = await Assert.ThrowsAsync<PolicyDeniedException>(() => service.OpenDownloadAsync(doc.Id, CancellationToken.None));
            Assert.Equal("document.scan_not_clean", denied.ReasonCode);
        }
    }

    [Fact]
    public async Task Quarantine_blocks_all_access()
    {
        var (world, tenant) = await SetupAsync();
        await using var db = world.Context(tenant);
        var service = world.Documents(db, tenant);
        var doc = await service.UploadAsync(TestWorld.TextUpload("fine"), CancellationToken.None);
        await service.QuarantineAsync(doc.Id, CancellationToken.None);
        var ex = await Assert.ThrowsAsync<PolicyDeniedException>(() => service.OpenDownloadAsync(doc.Id, CancellationToken.None));
        Assert.Equal("document.quarantined", ex.ReasonCode);
    }

    [Fact]
    public async Task Stored_documents_carry_required_metadata()
    {
        var (world, tenant) = await SetupAsync();
        await using var db = world.Context(tenant);
        var created = await world.Documents(db, tenant).UploadAsync(TestWorld.TextUpload("fine"), CancellationToken.None);
        var doc = await db.StoredDocuments.AsNoTracking().SingleAsync(d => d.Id == created.Id);
        Assert.Equal(tenant.TenantId, doc.TenantId);
        Assert.Equal(DataClassification.ConfidentialCommercial, doc.Classification);
        Assert.Equal(tenant.UserId, doc.DeclaredByUserId);
        Assert.NotEqual(Guid.Empty, doc.RetentionPolicyId);
        Assert.Equal(1, doc.ObjectVersion);
        Assert.Equal(64, doc.Sha256.Length);
        Assert.StartsWith($"tenants/{tenant.TenantId:N}/", doc.StorageKey, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(doc.CorrelationId));
    }
}
