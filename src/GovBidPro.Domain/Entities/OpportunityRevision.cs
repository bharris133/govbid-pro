using GovBidPro.Domain.Enums;

namespace GovBidPro.Domain.Entities;

/// <summary>Immutable record of a source change or amendment (handoff §5: do not silently overwrite).</summary>
public sealed class OpportunityRevision
{
    private OpportunityRevision()
    {
        ContentHash = string.Empty;
        SnapshotJson = "{}";
    }

    public OpportunityRevision(Guid id, Guid opportunityId, int revisionNumber, RevisionChangeKind changeKind, string contentHash, string snapshotJson, DateTimeOffset? sourceUpdatedAt, Guid importRunId, DateTimeOffset recordedAt)
    {
        Id = id;
        OpportunityId = opportunityId;
        RevisionNumber = revisionNumber;
        ChangeKind = changeKind;
        ContentHash = contentHash;
        SnapshotJson = snapshotJson;
        SourceUpdatedAt = sourceUpdatedAt;
        ImportRunId = importRunId;
        RecordedAt = recordedAt;
    }

    public Guid Id { get; private set; }

    public Guid OpportunityId { get; private set; }

    public int RevisionNumber { get; private set; }

    public RevisionChangeKind ChangeKind { get; private set; }

    public string ContentHash { get; private set; }

    /// <summary>Normalized source snapshot (public data only).</summary>
    public string SnapshotJson { get; private set; }

    public DateTimeOffset? SourceUpdatedAt { get; private set; }

    public Guid ImportRunId { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }
}
