namespace GovBidPro.Domain.ValueObjects;

/// <summary>
/// A response deadline as published by the source (handoff §5): a timezone-aware instant plus the
/// original source text and time-zone designation, so the user can verify the agency's wording.
/// The solicitation itself remains authoritative.
/// </summary>
public sealed record ResponseDeadline(DateTimeOffset? DueAt, string? OriginalText, string? SourceTimeZone)
{
    public static ResponseDeadline None { get; } = new(null, null, null);
}
