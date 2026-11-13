using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Opportunities;
using GovBidPro.Application.Retention;
using GovBidPro.Domain.Common;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using GovBidPro.Domain.ValueObjects;

namespace GovBidPro.UnitTests;

public sealed class DomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static OpportunityContent Content(string title, SourceNoticeType type = SourceNoticeType.Solicitation) =>
        new(title, "SOL-1", "desc", "Agency", null, type, "Solicitation", ResponseType.Rfp, "v1", "541511", null, null, "VA", "USA", Now, Now,
            new ResponseDeadline(Now.AddDays(10), "Dec 1, 4 PM ET", "America/New_York"), null, null);

    [Fact]
    public void Opportunity_records_revisions_and_ignores_unchanged_content()
    {
        var opportunity = new Opportunity(Guid.NewGuid(), "fixture", "X-1", isLiveSource: false);
        var first = Content("RFP one");
        var (hash1, snap1) = OpportunityImportService.ComputeContentHash(first);
        var created = opportunity.ApplySourceContent(first, hash1, snap1, Guid.NewGuid(), Now);
        Assert.Equal(RevisionChangeKind.Created, created!.ChangeKind);

        Assert.Null(opportunity.ApplySourceContent(first, hash1, snap1, Guid.NewGuid(), Now.AddHours(1)));
        Assert.Equal(Now.AddHours(1), opportunity.LastRetrievedAt);

        var amended = Content("RFP one (amended)");
        var (hash2, snap2) = OpportunityImportService.ComputeContentHash(amended);
        var revision = opportunity.ApplySourceContent(amended, hash2, snap2, Guid.NewGuid(), Now.AddHours(2));
        Assert.Equal(RevisionChangeKind.Amended, revision!.ChangeKind);
        Assert.Equal(2, opportunity.RevisionNumber);
        Assert.True(opportunity.IsAmended);
        Assert.Equal(2, opportunity.Revisions.Count);
    }

    [Fact]
    public void User_override_of_response_type_survives_amendments()
    {
        var opportunity = new Opportunity(Guid.NewGuid(), "fixture", "X-2", false);
        var (h1, s1) = OpportunityImportService.ComputeContentHash(Content("Solicitation A"));
        opportunity.ApplySourceContent(Content("Solicitation A"), h1, s1, Guid.NewGuid(), Now);
        opportunity.OverrideResponseType(ResponseType.Rfq);
        var (h2, s2) = OpportunityImportService.ComputeContentHash(Content("Solicitation A v2"));
        opportunity.ApplySourceContent(Content("Solicitation A v2"), h2, s2, Guid.NewGuid(), Now);
        Assert.Equal(ResponseType.Rfq, opportunity.ResponseType);
        Assert.Equal(ResponseTypeBasis.UserOverride, opportunity.ResponseTypeBasis);
    }

    [Fact]
    public void Restricted_documents_cannot_be_constructed()
    {
        Assert.Throws<DomainException>(() => new StoredDocument(Guid.NewGuid(), Guid.NewGuid(), "f.txt", "text/plain", 1, "h", "k", DataClassification.RestrictedPendingReview, "u", Now, "s", Guid.NewGuid(), ScanStatus.Clean, Now, "c"));
        Assert.Throws<DomainException>(() => new StoredDocument(Guid.NewGuid(), Guid.NewGuid(), "f.txt", "text/plain", 1, "h", "k", DataClassification.ProhibitedClassified, "u", Now, "s", Guid.NewGuid(), ScanStatus.Clean, Now, "c"));
    }

    [Fact]
    public void Document_lifecycle_respects_recovery_window_and_legal_hold()
    {
        var doc = new StoredDocument(Guid.NewGuid(), Guid.NewGuid(), "f.txt", "text/plain", 1, "h", "k", DataClassification.ConfidentialCommercial, "u", Now, "s", Guid.NewGuid(), ScanStatus.ScannerNotConfigured, Now, "c");
        Assert.False(doc.IsDownloadable);
        doc.RecordScanResult(ScanStatus.Clean);
        Assert.True(doc.IsDownloadable);

        doc.SoftDelete(Now, Now.AddDays(30));
        Assert.False(doc.IsEligibleForPurge(Now.AddDays(29)));
        Assert.True(doc.IsEligibleForPurge(Now.AddDays(30)));
        Assert.Throws<DomainException>(() => doc.Restore(Now.AddDays(31)));

        doc.SetLegalHold(true);
        Assert.False(doc.IsEligibleForPurge(Now.AddDays(31)));
        Assert.Throws<DomainException>(() => doc.MarkPurged(Now.AddDays(31)));
    }

    [Fact]
    public void Infected_scan_quarantines_document()
    {
        var doc = new StoredDocument(Guid.NewGuid(), Guid.NewGuid(), "f.txt", "text/plain", 1, "h", "k", DataClassification.ConfidentialCommercial, "u", Now, "s", Guid.NewGuid(), ScanStatus.Pending, Now, "c");
        doc.RecordScanResult(ScanStatus.Infected);
        Assert.Equal(DocumentState.Quarantined, doc.State);
        Assert.False(doc.IsDownloadable);
    }

    [Fact]
    public void Tenant_context_fails_closed_and_cannot_be_reassigned()
    {
        var holder = new TenantContextHolder();
        Assert.False(holder.HasTenant);
        Assert.Throws<InvalidOperationException>(() => holder.TenantId);
        holder.SetTenant(Guid.NewGuid(), "user", CompanyRole.Reviewer);
        Assert.Throws<InvalidOperationException>(() => holder.SetTenant(Guid.NewGuid(), "other", CompanyRole.CompanyAdmin));
        Assert.Throws<InvalidOperationException>(holder.SetSystem);
    }

    [Fact]
    public void Retention_configuration_must_define_every_policy()
    {
        var empty = new RetentionOptions();
        var errors = RetentionPolicyKeys.Validate(empty);
        Assert.Contains(errors, e => e.Contains("ApprovalStatus", StringComparison.Ordinal));
        foreach (var key in RetentionPolicyKeys.Required)
        {
            Assert.Contains(errors, e => e.Contains(key, StringComparison.Ordinal));
        }

        var missingPeriod = new RetentionOptions { ApprovalStatus = "x" };
        foreach (var key in RetentionPolicyKeys.Required)
        {
            missingPeriod.Policies[key] = new RetentionPeriodOptions();
        }

        Assert.Equal(RetentionPolicyKeys.Required.Count, RetentionPolicyKeys.Validate(missingPeriod).Count);
    }
}
