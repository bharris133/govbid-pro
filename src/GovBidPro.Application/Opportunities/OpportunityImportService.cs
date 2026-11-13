using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GovBidPro.Application.Abstractions;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using GovBidPro.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GovBidPro.Application.Opportunities;

public sealed record ImportRunSummary(Guid? RunId, ImportRunStatus Status, int Pages, int Created, int Amended, int Unchanged, int Rejected, string? ErrorCode);

/// <summary>
/// Bounded, idempotent, resumable import of public notices (handoff §6.2).
/// Each page and its checkpoint are saved atomically, so an interrupted run resumes after the last
/// persisted page. Content changes create revisions rather than overwriting history.
/// </summary>
public sealed class OpportunityImportService(IAppDbContext db, TimeProvider clock, ILogger<OpportunityImportService> logger)
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new() { WriteIndented = false };

    public async Task<ImportRunSummary> RunAsync(IOpportunitySource source, int maxPages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPages, 1);

        var now = clock.GetUtcNow();
        var state = await db.SourceSyncStates.SingleOrDefaultAsync(s => s.SourceKey == source.SourceKey, cancellationToken);
        if (state is null)
        {
            state = new SourceSyncState(source.SourceKey, source.IsLive);
            db.SourceSyncStates.Add(state);
        }

        if (state.RetryNotBefore is { } notBefore && notBefore > now)
        {
            logger.LogInformation("Import for {SourceKey} skipped until {RetryNotBefore} (backoff).", source.SourceKey, notBefore);
            return new ImportRunSummary(null, ImportRunStatus.RateLimited, 0, 0, 0, 0, 0, "backoff_active");
        }

        state.RecordAttempt(now);
        var run = new ImportRun(Guid.NewGuid(), source.SourceKey, now, state.Cursor);
        db.ImportRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        var cursor = state.Cursor is null ? null : new OpportunityImportCursor(state.Cursor);
        for (var pageNumber = 0; pageNumber < maxPages; pageNumber++)
        {
            var stopwatch = Stopwatch.StartNew();
            OpportunityImportPage page;
            try
            {
                page = await source.FetchAsync(cursor, cancellationToken);
            }
            catch (OpportunitySourceRateLimitedException ex)
            {
                return await FailAsync(state, run, ImportRunStatus.RateLimited, "rate_limited", "Source rate limit reached.", clock.GetUtcNow().Add(ex.RetryAfter), cancellationToken);
            }
            catch (OpportunitySourceException ex)
            {
                logger.LogWarning(ex, "Opportunity source {SourceKey} failed on page {Page}.", source.SourceKey, pageNumber + 1);
                var status = run.PagesProcessed > 0 ? ImportRunStatus.PartiallySucceeded : ImportRunStatus.Failed;
                var backoff = clock.GetUtcNow().Add(BackoffFor(state.ConsecutiveFailures + 1));
                return await FailAsync(state, run, status, "source_error", "Source request failed; see server logs.", backoff, cancellationToken);
            }

            var (created, amended, unchanged, rejected) = await UpsertPageAsync(source, page.Items, run.Id, cancellationToken);
            run.RecordPage(created, amended, unchanged, rejected);
            state.Checkpoint(page.NextCursor?.Value, stopwatch.ElapsedMilliseconds);
            await db.SaveChangesAsync(cancellationToken);

            if (page.NextCursor is null)
            {
                var completedAt = clock.GetUtcNow();
                run.Complete(ImportRunStatus.Succeeded, completedAt);
                state.RecordSuccess(completedAt);
                await db.SaveChangesAsync(cancellationToken);
                return Summarize(run);
            }

            cursor = page.NextCursor;
        }

        // Page budget exhausted: the run is healthy and will resume from the saved cursor next time.
        var at = clock.GetUtcNow();
        run.Complete(ImportRunStatus.PartiallySucceeded, at, "page_budget_exhausted");
        state.RecordSuccess(at);
        await db.SaveChangesAsync(cancellationToken);
        return Summarize(run);
    }

    /// <summary>Content hash over normalized fields (excludes retrieval timestamps) for idempotency.</summary>
    public static (string Hash, string Snapshot) ComputeContentHash(OpportunityContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var snapshot = JsonSerializer.Serialize(content, SnapshotJsonOptions);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));
        return (hash, snapshot);
    }

    private static TimeSpan BackoffFor(int failures) => TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Min(failures, 6))));

    private static ImportRunSummary Summarize(ImportRun run) =>
        new(run.Id, run.Status, run.PagesProcessed, run.Created, run.Amended, run.Unchanged, run.Rejected, run.ErrorCode);

    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private static OpportunityContent? Normalize(ImportedOpportunity item)
    {
        var title = Clean(item.Title, 500);
        if (string.IsNullOrWhiteSpace(item.ExternalId) || title is null)
        {
            return null;
        }

        return new OpportunityContent(
            Title: title,
            SolicitationNumber: Clean(item.SolicitationNumber, 128),
            Description: Clean(item.Description, 20000),
            Agency: Clean(item.Agency, 300),
            Office: Clean(item.Office, 300),
            NoticeType: item.NoticeType,
            SourceNoticeTypeRaw: Clean(item.SourceNoticeTypeRaw, 100),
            InterpretedResponseType: ResponseTypeMapper.Map(item.NoticeType, title, item.SourceNoticeTypeRaw),
            ResponseTypeRuleVersion: ResponseTypeMapper.RuleVersion,
            NaicsCode: Clean(item.NaicsCode, 10),
            PscCode: Clean(item.PscCode, 10),
            SetAside: Clean(item.SetAside, 100),
            PlaceOfPerformanceState: Clean(item.PlaceOfPerformanceState, 10)?.ToUpperInvariant(),
            PlaceOfPerformanceCountry: Clean(item.PlaceOfPerformanceCountry, 10)?.ToUpperInvariant(),
            PostedAt: item.PostedAt,
            SourceUpdatedAt: item.SourceUpdatedAt,
            Deadline: new ResponseDeadline(item.ResponseDueAt, Clean(item.ResponseDueOriginalText, 200), Clean(item.ResponseDueTimeZone, 64)),
            SubmissionInstructions: Clean(item.SubmissionInstructions, 4000),
            SourceUrl: Clean(item.SourceUrl, 2000));
    }

    private async Task<(int Created, int Amended, int Unchanged, int Rejected)> UpsertPageAsync(IOpportunitySource source, IReadOnlyList<ImportedOpportunity> items, Guid runId, CancellationToken cancellationToken)
    {
        int created = 0, amended = 0, unchanged = 0, rejected = 0;
        var ids = items.Select(i => i.ExternalId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
        var existing = await db.Opportunities
            .Where(o => o.SourceKey == source.SourceKey && ids.Contains(o.ExternalId))
            .ToDictionaryAsync(o => o.ExternalId, StringComparer.Ordinal, cancellationToken);

        var retrievedAt = clock.GetUtcNow();
        foreach (var item in items)
        {
            var content = Normalize(item);
            if (content is null)
            {
                rejected++;
                logger.LogWarning("Rejected notice from {SourceKey}: missing required fields (external id present: {HasId}).", source.SourceKey, !string.IsNullOrWhiteSpace(item.ExternalId));
                continue;
            }

            var (hash, snapshot) = ComputeContentHash(content);
            var isNew = !existing.TryGetValue(item.ExternalId, out var opportunity);
            if (opportunity is null)
            {
                opportunity = new Opportunity(Guid.NewGuid(), source.SourceKey, item.ExternalId, source.IsLive);
                existing[item.ExternalId] = opportunity;
                db.Opportunities.Add(opportunity);
            }

            var revision = opportunity.ApplySourceContent(content, hash, snapshot, runId, retrievedAt);
            if (revision is null)
            {
                unchanged++;
                continue;
            }

            db.OpportunityRevisions.Add(revision);
            if (isNew && revision.ChangeKind == RevisionChangeKind.Created)
            {
                created++;
            }
            else
            {
                amended++;
            }
        }

        return (created, amended, unchanged, rejected);
    }

    private async Task<ImportRunSummary> FailAsync(SourceSyncState state, ImportRun run, ImportRunStatus status, string errorCode, string safeMessage, DateTimeOffset? retryNotBefore, CancellationToken cancellationToken)
    {
        state.RecordFailure(errorCode, safeMessage, retryNotBefore);
        run.Complete(status, clock.GetUtcNow(), errorCode);
        await db.SaveChangesAsync(cancellationToken);
        return Summarize(run);
    }
}
