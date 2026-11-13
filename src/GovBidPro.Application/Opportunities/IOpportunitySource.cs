using GovBidPro.Domain.Enums;

namespace GovBidPro.Application.Opportunities;

/// <summary>
/// An external notice source adapter (handoff §6.1). Each source (official SAM.gov API, fixtures,
/// other platforms) is a separate adapter with its own source-specific mapping. Scraping is not a
/// primary integration path and access controls must never be evaded.
/// </summary>
public interface IOpportunitySource
{
    /// <summary>Stable key stored with every record, e.g. "fixture" or "sam.gov".</summary>
    string SourceKey { get; }

    /// <summary>False for demo/fixture data. Non-live data must be labeled as such to users.</summary>
    bool IsLive { get; }

    Task<OpportunityImportPage> FetchAsync(OpportunityImportCursor? cursor, CancellationToken cancellationToken);
}

public sealed record OpportunityImportCursor(string? Value);

/// <param name="Items">Notices in this page.</param>
/// <param name="NextCursor">Cursor for the next page, or null when the source has no more pages now.</param>
public sealed record OpportunityImportPage(IReadOnlyList<ImportedOpportunity> Items, OpportunityImportCursor? NextCursor);

/// <summary>
/// A notice as mapped by a source adapter. <see cref="NoticeType"/> is the adapter's source-specific
/// normalization; <see cref="SourceNoticeTypeRaw"/> is the verbatim source value.
/// </summary>
public sealed record ImportedOpportunity
{
    public required string ExternalId { get; init; }

    public string? Title { get; init; }

    public string? SolicitationNumber { get; init; }

    public string? Description { get; init; }

    public string? Agency { get; init; }

    public string? Office { get; init; }

    public SourceNoticeType NoticeType { get; init; }

    public string? SourceNoticeTypeRaw { get; init; }

    public string? NaicsCode { get; init; }

    public string? PscCode { get; init; }

    public string? SetAside { get; init; }

    public string? PlaceOfPerformanceState { get; init; }

    public string? PlaceOfPerformanceCountry { get; init; }

    public DateTimeOffset? PostedAt { get; init; }

    public DateTimeOffset? SourceUpdatedAt { get; init; }

    public DateTimeOffset? ResponseDueAt { get; init; }

    public string? ResponseDueOriginalText { get; init; }

    public string? ResponseDueTimeZone { get; init; }

    public string? SubmissionInstructions { get; init; }

    public string? SourceUrl { get; init; }
}

/// <summary>Source failure. Message must be safe to persist (no credentials or raw payloads).</summary>
public class OpportunitySourceException : Exception
{
    public OpportunitySourceException()
    {
    }

    public OpportunitySourceException(string message)
        : base(message)
    {
    }

    public OpportunitySourceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The source asked us to slow down. The import stops and records the retry time.</summary>
public sealed class OpportunitySourceRateLimitedException : OpportunitySourceException
{
    public OpportunitySourceRateLimitedException()
    {
    }

    public OpportunitySourceRateLimitedException(string message)
        : base(message)
    {
    }

    public OpportunitySourceRateLimitedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public OpportunitySourceRateLimitedException(TimeSpan retryAfter)
        : base("Source rate limit reached.")
    {
        RetryAfter = retryAfter;
    }

    public TimeSpan RetryAfter { get; } = TimeSpan.FromMinutes(5);
}
