using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Common;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GovBidPro.Application.Opportunities;

public sealed record OpportunitySummaryDto(
    Guid Id,
    string Title,
    string? SolicitationNumber,
    string? Agency,
    SourceNoticeType NoticeType,
    string? SourceNoticeTypeRaw,
    ResponseType ResponseType,
    ResponseTypeBasis ResponseTypeBasis,
    string? NaicsCode,
    string? SetAside,
    string? PlaceOfPerformanceState,
    DateTimeOffset? PostedAt,
    DateTimeOffset? ResponseDueAt,
    string SourceKey,
    bool IsLiveSource,
    bool IsAmended,
    DateTimeOffset LastRetrievedAt);

public sealed record OpportunityDetailDto(
    Guid Id,
    string Title,
    string? SolicitationNumber,
    string? Description,
    string? Agency,
    string? Office,
    SourceNoticeType NoticeType,
    string? SourceNoticeTypeRaw,
    ResponseType ResponseType,
    ResponseTypeBasis ResponseTypeBasis,
    string? ResponseTypeRuleVersion,
    string? NaicsCode,
    string? PscCode,
    string? SetAside,
    string? PlaceOfPerformanceState,
    string? PlaceOfPerformanceCountry,
    DateTimeOffset? PostedAt,
    DateTimeOffset? SourceUpdatedAt,
    DateTimeOffset? ResponseDueAt,
    string? ResponseDueOriginalText,
    string? ResponseDueTimeZone,
    string? SubmissionInstructions,
    string? SourceUrl,
    string SourceKey,
    bool IsLiveSource,
    int RevisionNumber,
    bool IsAmended,
    DateTimeOffset FirstIngestedAt,
    DateTimeOffset LastRetrievedAt,
    IReadOnlyList<OpportunityRevisionDto> Revisions);

public sealed record OpportunityRevisionDto(int RevisionNumber, RevisionChangeKind ChangeKind, DateTimeOffset? SourceUpdatedAt, DateTimeOffset RecordedAt);

public sealed record SourceFreshnessDto(string SourceKey, bool IsLiveSource, DateTimeOffset? LastSuccessfulSyncAt, DateTimeOffset? LastAttemptAt, string? LastErrorCode);

public sealed record OpportunitySearchResult(PagedResult<OpportunitySummaryDto> Results, IReadOnlyList<SourceFreshnessDto> Sources);

/// <summary>Read side for public opportunity data. Requires an authenticated tenant member.</summary>
public sealed class OpportunityQueryService(IAppDbContext db, ITenantContext tenant)
{
    public const int MaxPageSize = 100;

    public async Task<OpportunitySearchResult> SearchAsync(OpportunitySearchCriteria criteria, int page, int pageSize, CancellationToken cancellationToken)
    {
        tenant.RequireTenant();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = OpportunityFilter.Apply(db.Opportunities.AsNoTracking(), criteria);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(o => o.PostedAt)
            .ThenBy(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OpportunitySummaryDto(
                o.Id, o.Title, o.SolicitationNumber, o.Agency, o.NoticeType, o.SourceNoticeTypeRaw, o.ResponseType, o.ResponseTypeBasis,
                o.NaicsCode, o.SetAside, o.PlaceOfPerformanceState, o.PostedAt, o.Deadline.DueAt, o.SourceKey, o.IsLiveSource,
                o.RevisionNumber > 1, o.LastRetrievedAt))
            .ToListAsync(cancellationToken);

        var sources = await db.SourceSyncStates.AsNoTracking()
            .OrderBy(s => s.SourceKey)
            .Select(s => new SourceFreshnessDto(s.SourceKey, s.IsLiveSource, s.LastSuccessfulSyncAt, s.LastAttemptAt, s.LastErrorCode))
            .ToListAsync(cancellationToken);

        return new OpportunitySearchResult(new PagedResult<OpportunitySummaryDto>(items, page, pageSize, total), sources);
    }

    public async Task<OpportunityDetailDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        tenant.RequireTenant();
        var o = await db.Opportunities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new ResourceNotFoundException();
        var revisions = await db.OpportunityRevisions.AsNoTracking()
            .Where(r => r.OpportunityId == id)
            .OrderBy(r => r.RevisionNumber)
            .Select(r => new OpportunityRevisionDto(r.RevisionNumber, r.ChangeKind, r.SourceUpdatedAt, r.RecordedAt))
            .ToListAsync(cancellationToken);

        return new OpportunityDetailDto(
            o.Id, o.Title, o.SolicitationNumber, o.Description, o.Agency, o.Office, o.NoticeType, o.SourceNoticeTypeRaw,
            o.ResponseType, o.ResponseTypeBasis, o.ResponseTypeRuleVersion, o.NaicsCode, o.PscCode, o.SetAside,
            o.PlaceOfPerformanceState, o.PlaceOfPerformanceCountry, o.PostedAt, o.SourceUpdatedAt, o.Deadline.DueAt,
            o.Deadline.OriginalText, o.Deadline.SourceTimeZone, o.SubmissionInstructions, o.SourceUrl, o.SourceKey,
            o.IsLiveSource, o.RevisionNumber, o.IsAmended, o.FirstIngestedAt, o.LastRetrievedAt, revisions);
    }
}
