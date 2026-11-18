using System.Text;
using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Auditing;
using GovBidPro.Application.Common;
using GovBidPro.Application.Documents;
using GovBidPro.Application.Proposals;
using GovBidPro.Domain.Enums;
using GovBidPro.Testing;
using Microsoft.EntityFrameworkCore;

namespace GovBidPro.IntegrationTests;

/// <summary>P1.5: versions, proposal links, templates, rescans, purge of every version and exports.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class DocumentVersionTests(PostgresFixture postgres)
{
    private async Task<(TestWorld World, TenantContextHolder Tenant, Guid TenantId)> SetupAsync()
    {
        var world = new TestWorld(await postgres.CreateMigratedDatabaseAsync(), new MutableTimeProvider(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero)));
        await world.SyncRetentionAsync();
        var (tenantId, userId) = await world.CreateCompanyAsync("Versions Co");
        return (world, TestWorld.Tenant(tenantId, userId), tenantId);
    }

    private static UploadVersionCommand Version(string text, bool affirm = true, string? note = null, string fileName = "notes-v2.txt")
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return new UploadVersionCommand(fileName, "text/plain", bytes.Length, new MemoryStream(bytes), affirm, affirm, note);
    }

    private static async Task<string> ReadAsync(DocumentDownload download)
    {
        await using var stream = download.Content;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task New_versions_are_screened_become_current_and_keep_earlier_ones()
    {
        var (world, tenant, _) = await SetupAsync();
        await using var db = world.Context(tenant);
        var audit = new RecordingAuditLog();
        var service = world.Documents(db, tenant, audit);
        var first = await service.UploadAsync(TestWorld.TextUpload("first draft"), CancellationToken.None);
        Assert.Equal(1, first.CurrentVersion);

        world.Clock.Advance(TimeSpan.FromHours(1));
        var second = await service.UploadVersionAsync(first.Id, Version("second draft", note: "Added pricing"), CancellationToken.None);
        Assert.Equal(2, second.CurrentVersion);
        Assert.Equal("notes-v2.txt", second.FileName);
        Assert.Equal(2, world.Storage.Objects.Count);
        Assert.Contains(audit.Entries, e => e.Action == AuditActions.DocumentVersionAdded && e.Reason == "v2");

        var marked = await Assert.ThrowsAsync<PolicyDeniedException>(() => service.UploadVersionAsync(first.Id, Version("CUI//SP-PRVCY\nx"), CancellationToken.None));
        Assert.StartsWith("upload.suspected_restricted_content", marked.ReasonCode, StringComparison.Ordinal);
        var unaffirmed = await Assert.ThrowsAsync<PolicyDeniedException>(() => service.UploadVersionAsync(first.Id, Version("third", affirm: false), CancellationToken.None));
        Assert.Equal("upload.declaration_missing", unaffirmed.ReasonCode);
        Assert.Equal(2, world.Storage.Objects.Count);

        var versions = await service.ListVersionsAsync(first.Id, CancellationToken.None);
        Assert.Equal([2, 1], versions.Select(v => v.Number));
        Assert.True(versions[0].IsCurrent);
        Assert.Equal("Added pricing", versions[0].Note);
        Assert.EndsWith("@example.test", versions[1].UploadedBy, StringComparison.Ordinal);

        Assert.Equal("second draft", await ReadAsync(await service.OpenDownloadAsync(first.Id, CancellationToken.None)));
        Assert.Equal("first draft", await ReadAsync(await service.OpenVersionDownloadAsync(first.Id, 1, CancellationToken.None)));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.OpenVersionDownloadAsync(first.Id, 9, CancellationToken.None));

        await service.DeleteAsync(first.Id, CancellationToken.None);
        await Assert.ThrowsAsync<ConflictException>(() => service.UploadVersionAsync(first.Id, Version("late"), CancellationToken.None));
        await Assert.ThrowsAsync<PolicyDeniedException>(() => service.OpenVersionDownloadAsync(first.Id, 1, CancellationToken.None));
    }

    [Fact]
    public async Task Versions_are_private_to_the_company_and_reviewers_cannot_upload()
    {
        var (world, tenant, tenantId) = await SetupAsync();
        Guid id;
        await using (var db = world.Context(tenant))
        {
            id = (await world.Documents(db, tenant).UploadAsync(TestWorld.TextUpload("ours"), CancellationToken.None)).Id;
        }

        var reviewerId = await world.AddMemberAsync(tenantId, CompanyRole.Reviewer);
        var reviewer = TestWorld.Tenant(tenantId, reviewerId, CompanyRole.Reviewer);
        await using (var db = world.Context(reviewer))
        {
            var service = world.Documents(db, reviewer);
            await Assert.ThrowsAsync<PolicyDeniedException>(() => service.UploadVersionAsync(id, Version("x"), CancellationToken.None));
            await Assert.ThrowsAsync<PolicyDeniedException>(() => service.PlaceAsync(id, new PlaceDocumentCommand(null, true), CancellationToken.None));
            Assert.Single(await service.ListVersionsAsync(id, CancellationToken.None));
        }

        var (otherTenant, otherUser) = await world.CreateCompanyAsync("Other Co");
        var other = TestWorld.Tenant(otherTenant, otherUser);
        await using (var db = world.Context(other))
        {
            var service = world.Documents(db, other);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.ListVersionsAsync(id, CancellationToken.None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.UploadVersionAsync(id, Version("x"), CancellationToken.None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.OpenVersionDownloadAsync(id, 1, CancellationToken.None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.CopyAsync(id, new CopyDocumentCommand(null), CancellationToken.None));
        }

        Assert.Single(world.Storage.Objects);
    }

    [Fact]
    public async Task Templates_can_be_copied_into_a_proposal_and_links_must_be_the_companys_own()
    {
        var (world, tenant, _) = await SetupAsync();
        await world.ImportAsync(world.Notice("D1"));
        var opportunity = await world.OpportunityIdAsync("D1");
        var (otherTenant, otherUser) = await world.CreateCompanyAsync("Other Co");
        var other = TestWorld.Tenant(otherTenant, otherUser);
        Guid foreignProposal;
        await using (var db = world.Context(other))
        {
            foreignProposal = (await world.Proposals(db, other).CreateAsync(new CreateProposalCommand(opportunity, null, null), CancellationToken.None)).Id;
        }

        await using (var db = world.Context(tenant))
        {
            var proposal = (await world.Proposals(db, tenant).CreateAsync(new CreateProposalCommand(opportunity, null, null), CancellationToken.None)).Id;
            var service = world.Documents(db, tenant);
            var upload = TestWorld.TextUpload("Capability statement boilerplate") with { IsTemplate = true };
            var template = await service.UploadAsync(upload, CancellationToken.None);
            Assert.True(template.IsTemplate);
            Assert.Equal(template.Id, Assert.Single(await service.ListAsync(new DocumentFilter(TemplatesOnly: true), CancellationToken.None)).Id);

            var foreign = await Assert.ThrowsAsync<RequestValidationException>(() => service.CopyAsync(template.Id, new CopyDocumentCommand(foreignProposal), CancellationToken.None));
            Assert.Equal("document.proposal_not_found", foreign.ReasonCode);
            await Assert.ThrowsAsync<RequestValidationException>(() => service.PlaceAsync(template.Id, new PlaceDocumentCommand(foreignProposal, true), CancellationToken.None));

            var copy = await service.CopyAsync(template.Id, new CopyDocumentCommand(proposal), CancellationToken.None);
            Assert.NotEqual(template.Id, copy.Id);
            Assert.False(copy.IsTemplate);
            Assert.Equal(proposal, copy.ProposalId);
            Assert.Equal(template.Classification, copy.Classification);
            Assert.Equal("Capability statement boilerplate", await ReadAsync(await service.OpenDownloadAsync(copy.Id, CancellationToken.None)));
            Assert.Equal(copy.Id, Assert.Single(await service.ListAsync(new DocumentFilter(proposal), CancellationToken.None)).Id);

            // Editing the copy leaves the template untouched.
            await service.UploadVersionAsync(copy.Id, Version("Tailored for this bid"), CancellationToken.None);
            Assert.Equal("Capability statement boilerplate", await ReadAsync(await service.OpenDownloadAsync(template.Id, CancellationToken.None)));

            var unmarked = await service.PlaceAsync(template.Id, new PlaceDocumentCommand(proposal, false), CancellationToken.None);
            Assert.False(unmarked.IsTemplate);
            Assert.Equal(2, (await service.ListAsync(new DocumentFilter(proposal), CancellationToken.None)).Count);
        }
    }

    [Fact]
    public async Task Unscanned_versions_are_blocked_until_a_rescan_clears_them_and_malware_quarantines()
    {
        var (world, tenant, _) = await SetupAsync();
        world.Scanner = new FixedMalwareScanner(ScanStatus.Pending);
        Guid cleanLater;
        Guid infectedLater;
        await using (var db = world.Context(tenant))
        {
            var service = world.Documents(db, tenant);
            cleanLater = (await service.UploadAsync(TestWorld.TextUpload("fine"), CancellationToken.None)).Id;
            var denied = await Assert.ThrowsAsync<PolicyDeniedException>(() => service.OpenDownloadAsync(cleanLater, CancellationToken.None));
            Assert.Equal("document.scan_not_clean", denied.ReasonCode);
            await Assert.ThrowsAsync<PolicyDeniedException>(() => service.OpenVersionDownloadAsync(cleanLater, 1, CancellationToken.None));
            await Assert.ThrowsAsync<PolicyDeniedException>(() => service.CopyAsync(cleanLater, new CopyDocumentCommand(null), CancellationToken.None));
        }

        var system = PostgresFixture.SystemContext();
        await using (var db = world.Context(system))
        {
            // Scanner still unavailable: nothing changes.
            Assert.Equal(new RescanSummary(0, 0, 0, 1, true), await world.Rescan(db, system).RunAsync(10, CancellationToken.None));
            world.Scanner = new FixedMalwareScanner(ScanStatus.ScannerNotConfigured);
            Assert.False((await world.Rescan(db, system).RunAsync(10, CancellationToken.None)).ScannerAvailable);
            world.Scanner = new FixedMalwareScanner(ScanStatus.Clean);
            Assert.Equal(1, (await world.Rescan(db, system).RunAsync(10, CancellationToken.None)).Clean);
            Assert.Equal(0, (await world.Rescan(db, system).RunAsync(10, CancellationToken.None)).Scanned);
        }

        world.Scanner = new FixedMalwareScanner(ScanStatus.Pending);
        await using (var db = world.Context(tenant))
        {
            var service = world.Documents(db, tenant);
            Assert.Equal("fine", await ReadAsync(await service.OpenDownloadAsync(cleanLater, CancellationToken.None)));
            infectedLater = (await service.UploadAsync(TestWorld.TextUpload("looks harmless"), CancellationToken.None)).Id;
        }

        world.Scanner = new FixedMalwareScanner(ScanStatus.Infected);
        await using (var db = world.Context(system))
        {
            Assert.Equal(1, (await world.Rescan(db, system).RunAsync(10, CancellationToken.None)).Infected);
            Assert.True(await db.AuditEvents.AnyAsync(e => e.Action == AuditActions.DocumentScanned && e.ResourceId == infectedLater.ToString()));
        }

        await using (var db = world.Context(tenant))
        {
            var service = world.Documents(db, tenant);
            var denied = await Assert.ThrowsAsync<PolicyDeniedException>(() => service.OpenDownloadAsync(infectedLater, CancellationToken.None));
            Assert.Equal("document.quarantined", denied.ReasonCode);
            Assert.Equal(DocumentState.Quarantined, (await service.ListAsync(CancellationToken.None)).Single(d => d.Id == infectedLater).State);
            await Assert.ThrowsAsync<InvalidOperationException>(() => world.Rescan(db, tenant).RunAsync(10, CancellationToken.None));
        }
    }

    [Fact]
    public async Task Platform_scanner_verdicts_are_read_by_storage_key_and_gate_downloads()
    {
        var (world, tenant, _) = await SetupAsync();
        var platform = new FakePlatformMalwareScanner();
        world.Scanner = platform;
        Guid clean;
        Guid malicious;
        await using (var db = world.Context(tenant))
        {
            var service = world.Documents(db, tenant);
            clean = (await service.UploadAsync(TestWorld.TextUpload("fine"), CancellationToken.None)).Id;
            malicious = (await service.UploadAsync(TestWorld.TextUpload("bad"), CancellationToken.None)).Id;
            Assert.All(await service.ListAsync(CancellationToken.None), d => Assert.Equal(ScanStatus.Pending, d.ScanStatus));
            var denied = await Assert.ThrowsAsync<PolicyDeniedException>(() => service.OpenDownloadAsync(clean, CancellationToken.None));
            Assert.Equal("document.scan_not_clean", denied.ReasonCode);
        }

        var system = PostgresFixture.SystemContext();
        await using (var db = world.Context(system))
        {
            // No verdict from the platform yet: both stay pending.
            Assert.Equal(new RescanSummary(0, 0, 0, 2, true), await world.Rescan(db, system).RunAsync(10, CancellationToken.None));
            var keys = await db.DocumentVersions.Where(v => v.DocumentId == clean || v.DocumentId == malicious).ToDictionaryAsync(v => v.DocumentId, v => v.StorageKey);
            platform.Verdicts[keys[clean]] = ScanStatus.Clean;
            platform.Verdicts[keys[malicious]] = ScanStatus.Infected;
            var summary = await world.Rescan(db, system).RunAsync(10, CancellationToken.None);
            Assert.Equal((2, 1, 1), (summary.Scanned, summary.Clean, summary.Infected));
        }

        // Verdicts came from the platform; the application never re-read or re-scanned the content.
        Assert.Equal(2, platform.ContentScans);
        await using (var db = world.Context(tenant))
        {
            var service = world.Documents(db, tenant);
            Assert.Equal("fine", await ReadAsync(await service.OpenDownloadAsync(clean, CancellationToken.None)));
            var denied = await Assert.ThrowsAsync<PolicyDeniedException>(() => service.OpenDownloadAsync(malicious, CancellationToken.None));
            Assert.Equal("document.quarantined", denied.ReasonCode);
        }
    }

    [Fact]
    public async Task Purge_removes_every_stored_version()
    {
        var (world, tenant, _) = await SetupAsync();
        await using (var db = world.Context(tenant))
        {
            var service = world.Documents(db, tenant);
            var doc = await service.UploadAsync(TestWorld.TextUpload("one"), CancellationToken.None);
            await service.UploadVersionAsync(doc.Id, Version("two"), CancellationToken.None);
            await service.UploadVersionAsync(doc.Id, Version("three"), CancellationToken.None);
            await service.DeleteAsync(doc.Id, CancellationToken.None);
        }

        Assert.Equal(3, world.Storage.Objects.Count);
        world.Clock.Advance(TimeSpan.FromDays(31));
        var system = PostgresFixture.SystemContext();
        await using (var db = world.Context(system))
        {
            Assert.Equal(1, (await world.Purge(db, system).RunOnceAsync(CancellationToken.None)).Purged);
        }

        Assert.Empty(world.Storage.Objects);
    }

    [Fact]
    public async Task Exports_cover_the_matrix_neutralize_formulas_and_stay_tenant_scoped()
    {
        var (world, tenant, _) = await SetupAsync();
        await world.ImportAsync(world.Notice("E1", "Request for Proposal: Fleet maintenance"));
        var opportunity = await world.OpportunityIdAsync("E1");
        Guid proposalId;
        await using (var db = world.Context(tenant))
        {
            var audit = new RecordingAuditLog();
            var proposals = world.Proposals(db, tenant);
            var proposal = await proposals.CreateAsync(new CreateProposalCommand(opportunity, null, null), CancellationToken.None);
            proposalId = proposal.Id;
            await proposals.AddRequirementAsync(proposalId, new RequirementCommand("L.1", "=HYPERLINK(\"http://x\",\"click\")", null, null, true, proposal.Sections[0].Id, null, null, null), CancellationToken.None);
            await proposals.AddRequirementAsync(proposalId, new RequirementCommand("L.2", "Provide \"key\" staff, with résumés", "RFP p. 12", "Staffing", false, null, null, null, "See volume 2", ComplianceStatus.Compliant), CancellationToken.None);

            var exports = new ProposalExportService(proposals, audit, world.Clock);
            var csv = await exports.ComplianceMatrixCsvAsync(proposalId, CancellationToken.None);
            Assert.Equal("text/csv", csv.ContentType);
            Assert.Equal("request-for-proposal-fleet-maintenance-compliance-matrix.csv", csv.FileName);
            var text = Encoding.UTF8.GetString(csv.Content);
            var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(3, lines.Length);
            Assert.StartsWith("﻿Reference,Requirement,", lines[0], StringComparison.Ordinal);
            Assert.Contains("L.1,\"'=HYPERLINK(\"\"http://x\"\",\"\"click\"\")\"", lines[1], StringComparison.Ordinal);
            Assert.Contains("\"Provide \"\"key\"\" staff, with résumés\",RFP p. 12,Staffing,No,Compliant", lines[2], StringComparison.Ordinal);

            var outline = Encoding.UTF8.GetString((await exports.OutlineMarkdownAsync(proposalId, CancellationToken.None)).Content);
            Assert.Contains("# Request for Proposal: Fleet maintenance", outline, StringComparison.Ordinal);
            Assert.Contains("- [ ] L.1 (mandatory)", outline, StringComparison.Ordinal);
            Assert.Contains("- [x] L.2:", outline, StringComparison.Ordinal);
            Assert.Contains("GovBid Pro does not submit responses", outline, StringComparison.Ordinal);
            Assert.Equal(2, audit.Entries.Count(e => e.Action == AuditActions.ExportGenerated));
        }

        var (otherTenant, otherUser) = await world.CreateCompanyAsync("Other Co");
        var other = TestWorld.Tenant(otherTenant, otherUser);
        await using (var db = world.Context(other))
        {
            var exports = new ProposalExportService(world.Proposals(db, other), new RecordingAuditLog(), world.Clock);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => exports.ComplianceMatrixCsvAsync(proposalId, CancellationToken.None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => exports.OutlineMarkdownAsync(proposalId, CancellationToken.None));
        }
    }
}
