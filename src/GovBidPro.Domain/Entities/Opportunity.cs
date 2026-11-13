using GovBidPro.Domain.Common;
using GovBidPro.Domain.Enums;
using GovBidPro.Domain.ValueObjects;

namespace GovBidPro.Domain.Entities;

/// <summary>
/// A sourced government notice. Global (shared across tenants) and always <see cref="DataClassification.Public"/>.
/// Company-specific data (assessments, proposals, saved searches) lives in tenant-owned entities.
/// Content is never silently overwritten: every change produces an <see cref="OpportunityRevision"/>.
/// </summary>
public sealed class Opportunity
{
    private readonly List<OpportunityRevision> _revisions = [];

    private Opportunity()
    {
        SourceKey = string.Empty;
        ExternalId = string.Empty;
        Title = string.Empty;
        ContentHash = string.Empty;
        Deadline = ResponseDeadline.None;
    }

    public Opportunity(Guid id, string sourceKey, string externalId, bool isLiveSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        Id = id;
        SourceKey = sourceKey;
        ExternalId = externalId;
        IsLiveSource = isLiveSource;
        Title = string.Empty;
        ContentHash = string.Empty;
        Deadline = ResponseDeadline.None;
    }

    public Guid Id { get; private set; }

    /// <summary>Identifies the adapter that produced this record, e.g. "fixture" or "sam.gov".</summary>
    public string SourceKey { get; private set; }

    /// <summary>The source's own notice identifier. Unique per <see cref="SourceKey"/>.</summary>
    public string ExternalId { get; private set; }

    /// <summary>False for fixture/demo data. Fixture data must never be presented as live government data.</summary>
    public bool IsLiveSource { get; private set; }

    public string? SolicitationNumber { get; private set; }

    public string Title { get; private set; }

    public string? Description { get; private set; }

    public string? Agency { get; private set; }

    public string? Office { get; private set; }

    /// <summary>Normalized source notice type. See <see cref="SourceNoticeTypeRaw"/> for the verbatim value.</summary>
    public SourceNoticeType NoticeType { get; private set; }

    public string? SourceNoticeTypeRaw { get; private set; }

    /// <summary>Interpreted response type. Distinct from <see cref="NoticeType"/>.</summary>
    public ResponseType ResponseType { get; private set; }

    public ResponseTypeBasis ResponseTypeBasis { get; private set; }

    /// <summary>Version of the mapping rules that produced <see cref="ResponseType"/>, when rule-based.</summary>
    public string? ResponseTypeRuleVersion { get; private set; }

    public string? NaicsCode { get; private set; }

    public string? PscCode { get; private set; }

    public string? SetAside { get; private set; }

    public string? PlaceOfPerformanceState { get; private set; }

    public string? PlaceOfPerformanceCountry { get; private set; }

    public DateTimeOffset? PostedAt { get; private set; }

    public DateTimeOffset? SourceUpdatedAt { get; private set; }

    public ResponseDeadline Deadline { get; private set; }

    /// <summary>Submission method/instructions as published. GovBid Pro never submits on the user's behalf.</summary>
    public string? SubmissionInstructions { get; private set; }

    public string? SourceUrl { get; private set; }

    public DateTimeOffset FirstIngestedAt { get; private set; }

    public DateTimeOffset LastRetrievedAt { get; private set; }

    public int RevisionNumber { get; private set; }

    /// <summary>Hash of the normalized source content; used for idempotent upserts.</summary>
    public string ContentHash { get; private set; }

    /// <summary>Sourced notices are always public data (§9B.1).</summary>
    public static DataClassification Classification => DataClassification.Public;

    public IReadOnlyCollection<OpportunityRevision> Revisions => _revisions;

    public bool IsAmended => RevisionNumber > 1;

    /// <summary>
    /// Applies source content. Returns the revision created, or null if the content is unchanged
    /// (in which case only the retrieval timestamp is updated).
    /// </summary>
    public OpportunityRevision? ApplySourceContent(OpportunityContent content, string contentHash, string snapshotJson, Guid importRunId, DateTimeOffset retrievedAt)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        LastRetrievedAt = retrievedAt;
        if (string.Equals(contentHash, ContentHash, StringComparison.Ordinal))
        {
            return null;
        }

        var kind = RevisionNumber == 0 ? RevisionChangeKind.Created : RevisionChangeKind.Amended;
        if (kind == RevisionChangeKind.Created)
        {
            FirstIngestedAt = retrievedAt;
        }

        Title = content.Title;
        SolicitationNumber = content.SolicitationNumber;
        Description = content.Description;
        Agency = content.Agency;
        Office = content.Office;
        NoticeType = content.NoticeType;
        SourceNoticeTypeRaw = content.SourceNoticeTypeRaw;
        NaicsCode = content.NaicsCode;
        PscCode = content.PscCode;
        SetAside = content.SetAside;
        PlaceOfPerformanceState = content.PlaceOfPerformanceState;
        PlaceOfPerformanceCountry = content.PlaceOfPerformanceCountry;
        PostedAt = content.PostedAt;
        SourceUpdatedAt = content.SourceUpdatedAt;
        Deadline = content.Deadline;
        SubmissionInstructions = content.SubmissionInstructions;
        SourceUrl = content.SourceUrl;
        ContentHash = contentHash;
        RevisionNumber++;

        if (ResponseTypeBasis != ResponseTypeBasis.UserOverride)
        {
            ResponseType = content.InterpretedResponseType;
            ResponseTypeBasis = ResponseTypeBasis.MappingRule;
            ResponseTypeRuleVersion = content.ResponseTypeRuleVersion;
        }

        var revision = new OpportunityRevision(Guid.NewGuid(), Id, RevisionNumber, kind, contentHash, snapshotJson, content.SourceUpdatedAt, importRunId, retrievedAt);
        _revisions.Add(revision);
        return revision;
    }

    public void OverrideResponseType(ResponseType responseType)
    {
        ResponseType = responseType;
        ResponseTypeBasis = ResponseTypeBasis.UserOverride;
        ResponseTypeRuleVersion = null;
    }
}

/// <summary>Normalized content accepted from a source adapter.</summary>
public sealed record OpportunityContent(
    string Title,
    string? SolicitationNumber,
    string? Description,
    string? Agency,
    string? Office,
    SourceNoticeType NoticeType,
    string? SourceNoticeTypeRaw,
    ResponseType InterpretedResponseType,
    string ResponseTypeRuleVersion,
    string? NaicsCode,
    string? PscCode,
    string? SetAside,
    string? PlaceOfPerformanceState,
    string? PlaceOfPerformanceCountry,
    DateTimeOffset? PostedAt,
    DateTimeOffset? SourceUpdatedAt,
    ResponseDeadline Deadline,
    string? SubmissionInstructions,
    string? SourceUrl);
