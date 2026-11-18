using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Auditing;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GovBidPro.Application.Documents;

public sealed class DocumentRescanOptions
{
    public const string SectionName = "Documents:Rescan";

    /// <summary>
    /// Re-screens stored versions whose scan is Pending (scanner was unreachable, or a platform scanner such as
    /// Defender for Storage has not reported yet) or ScannerNotConfigured. With a platform scanner, keep the
    /// interval short (1 minute) because new uploads wait for this job before they can be downloaded.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 15;

    public int BatchSize { get; set; } = 50;
}

public sealed record RescanSummary(int Scanned, int Clean, int Infected, int StillPending, bool ScannerAvailable);

/// <summary>
/// Catches up malware screening after a scanner is configured or comes back. Runs in the system context.
/// An infected version quarantines the whole document. File names and content are never logged.
/// </summary>
public sealed class DocumentRescanService(IAppDbContext db, ITenantContext context, IObjectStorage storage, IMalwareScanner scanner, TimeProvider clock, ILogger<DocumentRescanService> logger)
{
    public async Task<RescanSummary> RunAsync(int batchSize, CancellationToken cancellationToken)
    {
        if (!context.IsSystem)
        {
            throw new InvalidOperationException("Document rescan must run in an explicit system context.");
        }

        var candidates = await db.DocumentVersions
            .Where(v => v.ScanStatus == ScanStatus.Pending || v.ScanStatus == ScanStatus.ScannerNotConfigured)
            .OrderBy(v => v.UploadedAt)
            .Take(Math.Clamp(batchSize, 1, 500))
            .ToListAsync(cancellationToken);

        int scanned = 0, clean = 0, infected = 0, pending = 0;
        foreach (var version in candidates)
        {
            var document = await db.StoredDocuments.SingleOrDefaultAsync(d => d.Id == version.DocumentId && d.TenantId == version.TenantId, cancellationToken);
            if (document is null || document.State == DocumentState.Purged)
            {
                continue;
            }

            ScanStatus result;
            try
            {
                result = await ScanStoredVersionAsync(version, cancellationToken);
            }
#pragma warning disable CA1031 // One unreadable object must not stop the batch; it stays pending and is retried.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                logger.LogWarning(ex, "Rescan could not read document {DocumentId} version {Version}.", version.DocumentId, version.Number);
                pending++;
                continue;
            }

            if (result == ScanStatus.ScannerNotConfigured)
            {
                return new RescanSummary(scanned, clean, infected, pending, ScannerAvailable: false);
            }

            if (result == ScanStatus.Pending)
            {
                pending++;
                continue;
            }

            var now = clock.GetUtcNow();
            version.RecordScanResult(result, now);
            if (version.Number == document.ObjectVersion)
            {
                document.RecordScanResult(result);
            }
            else if (result == ScanStatus.Infected)
            {
                document.Quarantine("malware-detected");
            }

            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), document.TenantId, null, AuditActions.DocumentScanned, "document", document.Id.ToString(), AuditOutcome.Success, $"v{version.Number}:{result}", "rescan-job", now));
            await db.SaveChangesAsync(cancellationToken);
            scanned++;
            if (result == ScanStatus.Infected)
            {
                infected++;
                logger.LogWarning("Rescan found malware in document {DocumentId} version {Version}; the document is quarantined.", document.Id, version.Number);
            }
            else
            {
                clean++;
            }
        }

        return new RescanSummary(scanned, clean, infected, pending, ScannerAvailable: true);
    }

    private async Task<ScanStatus> ScanStoredVersionAsync(DocumentVersion version, CancellationToken cancellationToken)
    {
        // Platform scanners (for example Defender for Storage) have already scanned the stored object; only the verdict is read.
        if (scanner is IStoredObjectMalwareScanner platform)
        {
            return await platform.GetVerdictAsync(version.StorageKey, cancellationToken);
        }

        await using var stream = await storage.OpenReadAsync(version.StorageKey, cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return await scanner.ScanAsync(buffer.ToArray(), version.FileName, cancellationToken);
    }
}
