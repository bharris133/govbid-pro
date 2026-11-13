using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Documents;
using GovBidPro.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GovBidPro.Application.Retention;

public sealed record PurgeSummary(int Purged, int Failed, int SkippedForLegalHold);

/// <summary>
/// Idempotent, resumable purge of soft-deleted documents past their recovery window (RET-02).
/// Runs in a system context. Purge covers live objects and database state only: objects may remain in
/// still-retained backups until normal expiry (RET-03), so this is never reported as "deleted everywhere".
/// </summary>
public sealed class RetentionPurgeService(IAppDbContext db, ITenantContext context, IObjectStorage storage, TimeProvider clock, IOptions<RetentionOptions> options, ILogger<RetentionPurgeService> logger)
{
    public async Task<PurgeSummary> RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!context.IsSystem)
        {
            throw new InvalidOperationException("Retention purge must run in an explicit system context.");
        }

        var now = clock.GetUtcNow();
        var batch = Math.Clamp(options.Value.PurgeJob.BatchSize, 1, 1000);
        var candidates = await db.StoredDocuments
            .Where(d => d.State == DocumentState.SoftDeleted && d.RecoverableUntil != null && d.RecoverableUntil <= now)
            .OrderBy(d => d.RecoverableUntil)
            .Take(batch)
            .ToListAsync(cancellationToken);

        int purged = 0, failed = 0, held = 0;
        foreach (var document in candidates)
        {
            var action = await db.RetentionActions
                .Where(a => a.ResourceId == document.Id && a.TenantId == document.TenantId && a.Action == RetentionActionType.Purge && a.Status != RetentionActionStatus.Completed && a.Status != RetentionActionStatus.Cancelled)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (document.LegalHold)
            {
                held++;
                continue;
            }

            try
            {
                await storage.DeleteAsync(document.StorageKey, cancellationToken);
                document.MarkPurged(now);
                action?.MarkCompleted(now);
                db.AuditEvents.Add(new Domain.Entities.AuditEvent(Guid.NewGuid(), document.TenantId, null, Auditing.AuditActions.DocumentPurge, "document", document.Id.ToString(), AuditOutcome.Success, "retention.recovery_window_expired", action?.CorrelationId ?? "retention-job", now));
                await db.SaveChangesAsync(cancellationToken);
                purged++;
            }
#pragma warning disable CA1031 // A single failure must not stop the batch; it is recorded and retried next run.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "Purge failed for document {DocumentId} (tenant {TenantId}).", document.Id, document.TenantId);
                action?.MarkFailed("storage_or_database_error");
                await db.SaveChangesAsync(cancellationToken);
                failed++;
            }
        }

        return new PurgeSummary(purged, failed, held);
    }
}
