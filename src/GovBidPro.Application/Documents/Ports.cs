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
