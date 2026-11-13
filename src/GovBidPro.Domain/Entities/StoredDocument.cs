using GovBidPro.Domain.Common;
using GovBidPro.Domain.Enums;

namespace GovBidPro.Domain.Entities;

/// <summary>
/// Metadata for a customer-uploaded object in private storage (handoff §9B.2). Carries tenant,
/// classification, declaration, retention and scan metadata used by authoritative access checks.
/// Document bodies are never stored in this table or in logs.
/// </summary>
public sealed class StoredDocument : ITenantOwned
{
    private StoredDocument()
    {
        FileName = string.Empty;
        ContentType = string.Empty;
        Sha256 = string.Empty;
        StorageKey = string.Empty;
        DeclaredByUserId = string.Empty;
        Source = string.Empty;
        CorrelationId = string.Empty;
    }

    public StoredDocument(
        Guid id,
        Guid tenantId,
        string fileName,
        string contentType,
        long sizeBytes,
        string sha256,
        string storageKey,
        DataClassification classification,
        string declaredByUserId,
        DateTimeOffset declaredAt,
        string source,
        Guid retentionPolicyId,
        ScanStatus scanStatus,
        DateTimeOffset createdAt,
        string correlationId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new DomainException("Tenant id is required.");
        }

        if (classification is DataClassification.RestrictedPendingReview or DataClassification.ProhibitedClassified)
        {
            // Release 1: restricted/prohibited content is rejected before storage (§9B.1, SEC-03).
            throw new DomainException("Restricted or prohibited content cannot be stored in ordinary storage.");
        }

        Id = id;
        TenantId = tenantId;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        StorageKey = storageKey;
        Classification = classification;
        DeclaredByUserId = declaredByUserId;
        DeclaredAt = declaredAt;
        Source = source;
        RetentionPolicyId = retentionPolicyId;
        ObjectVersion = 1;
        ScanStatus = scanStatus;
        State = scanStatus == ScanStatus.Infected ? DocumentState.Quarantined : DocumentState.Active;
        CreatedAt = createdAt;
        CorrelationId = correlationId;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string FileName { get; private set; }

    public string ContentType { get; private set; }

    public long SizeBytes { get; private set; }

    public string Sha256 { get; private set; }

    public string StorageKey { get; private set; }

    public DataClassification Classification { get; private set; }

    public string DeclaredByUserId { get; private set; }

    public DateTimeOffset DeclaredAt { get; private set; }

    public string Source { get; private set; }

    public Guid RetentionPolicyId { get; private set; }

    public int ObjectVersion { get; private set; }

    public ScanStatus ScanStatus { get; private set; }

    public DocumentState State { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public DateTimeOffset? RecoverableUntil { get; private set; }

    public DateTimeOffset? PurgedAt { get; private set; }

    public bool LegalHold { get; private set; }

    public string? QuarantineReason { get; private set; }

    public string CorrelationId { get; private set; }

    /// <summary>True when the object may be streamed to an authorized user.</summary>
    public bool IsDownloadable => State == DocumentState.Active && ScanStatus is ScanStatus.Clean or ScanStatus.DevelopmentBypass;

    public void SoftDelete(DateTimeOffset at, DateTimeOffset recoverableUntil)
    {
        if (State is DocumentState.Purged or DocumentState.SoftDeleted)
        {
            throw new DomainException($"Cannot delete a document in state {State}.");
        }

        State = DocumentState.SoftDeleted;
        DeletedAt = at;
        RecoverableUntil = recoverableUntil;
    }

    public void Restore(DateTimeOffset at)
    {
        if (State != DocumentState.SoftDeleted || RecoverableUntil is null || at > RecoverableUntil)
        {
            throw new DomainException("Document is not recoverable.");
        }

        State = DocumentState.Active;
        DeletedAt = null;
        RecoverableUntil = null;
    }

    /// <summary>Disables all access for suspected sensitive content (§9B.2 incident handling).</summary>
    public void Quarantine(string reasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        if (State == DocumentState.Purged)
        {
            throw new DomainException("Purged documents cannot be quarantined.");
        }

        State = DocumentState.Quarantined;
        QuarantineReason = reasonCode;
    }

    public bool IsEligibleForPurge(DateTimeOffset now) =>
        State == DocumentState.SoftDeleted && !LegalHold && RecoverableUntil is not null && RecoverableUntil <= now;

    public void MarkPurged(DateTimeOffset at)
    {
        if (LegalHold)
        {
            throw new DomainException("Legal hold prevents purge.");
        }

        State = DocumentState.Purged;
        PurgedAt = at;
    }

    public void SetLegalHold(bool hold) => LegalHold = hold;

    public void RecordScanResult(ScanStatus status)
    {
        ScanStatus = status;
        if (status == ScanStatus.Infected)
        {
            Quarantine("malware-detected");
        }
    }
}
