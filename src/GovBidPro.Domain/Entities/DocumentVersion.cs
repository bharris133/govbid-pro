using GovBidPro.Domain.Common;
using GovBidPro.Domain.Enums;

namespace GovBidPro.Domain.Entities;

/// <summary>
/// One immutable stored object of a document. The document row mirrors its current (highest) version;
/// earlier versions stay downloadable (when scanned clean) until the document is purged.
/// </summary>
public sealed class DocumentVersion : ITenantOwned
{
    public const int MaxNoteLength = 500;

    private DocumentVersion()
    {
        FileName = string.Empty;
        ContentType = string.Empty;
        Sha256 = string.Empty;
        StorageKey = string.Empty;
        UploadedByUserId = string.Empty;
    }

    public DocumentVersion(Guid id, Guid tenantId, Guid documentId, int number, string fileName, string contentType, long sizeBytes, string sha256, string storageKey, ScanStatus scanStatus, string uploadedByUserId, DateTimeOffset uploadedAt, string? note)
    {
        if (tenantId == Guid.Empty || documentId == Guid.Empty || number < 1)
        {
            throw new DomainException("Tenant, document and a positive version number are required.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadedByUserId);
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is { Length: > MaxNoteLength })
        {
            throw new DomainException($"Version note must be at most {MaxNoteLength} characters.");
        }

        Id = id;
        TenantId = tenantId;
        DocumentId = documentId;
        Number = number;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        StorageKey = storageKey;
        ScanStatus = scanStatus;
        UploadedByUserId = uploadedByUserId;
        UploadedAt = uploadedAt;
        Note = trimmed;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid DocumentId { get; private set; }

    public int Number { get; private set; }

    public string FileName { get; private set; }

    public string ContentType { get; private set; }

    public long SizeBytes { get; private set; }

    public string Sha256 { get; private set; }

    public string StorageKey { get; private set; }

    public ScanStatus ScanStatus { get; private set; }

    public DateTimeOffset? ScannedAt { get; private set; }

    public string UploadedByUserId { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    public string? Note { get; private set; }

    public bool IsClean => ScanStatus is ScanStatus.Clean or ScanStatus.DevelopmentBypass;

    public void RecordScanResult(ScanStatus status, DateTimeOffset at)
    {
        ScanStatus = status;
        ScannedAt = at;
    }
}
