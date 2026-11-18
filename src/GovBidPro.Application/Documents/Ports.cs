using GovBidPro.Domain.Enums;

namespace GovBidPro.Application.Documents;

/// <summary>Private object storage (handoff §2 Files). Implementations must never expose public URLs.</summary>
public interface IObjectStorage
{
    Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>Idempotent: deleting a missing object succeeds.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

/// <summary>Malware screening port. The default implementation reports "not configured" (deny downloads).</summary>
public interface IMalwareScanner
{
    Task<ScanStatus> ScanAsync(ReadOnlyMemory<byte> content, string fileName, CancellationToken cancellationToken);
}

/// <summary>
/// A scanner that inspects objects after they are written to storage (platform on-upload scanning, for example
/// Microsoft Defender for Storage, Amazon GuardDuty Malware Protection for S3 or a bucket-triggered scanner on
/// another cloud). <see cref="IMalwareScanner.ScanAsync"/> returns <see cref="ScanStatus.Pending"/> at upload
/// because the object does not exist yet; the rescan job later asks for the verdict by storage key, without
/// downloading the content. Until a verdict of Clean arrives the file stays undownloadable.
/// </summary>
public interface IStoredObjectMalwareScanner : IMalwareScanner
{
    /// <summary>Clean, Infected, or Pending when the platform has not reported (or could not scan) yet.</summary>
    Task<ScanStatus> GetVerdictAsync(string storageKey, CancellationToken cancellationToken);
}
