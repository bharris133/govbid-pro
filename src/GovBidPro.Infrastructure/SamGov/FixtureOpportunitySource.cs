using System.Globalization;
using System.Text.Json;
using GovBidPro.Application.Opportunities;
using GovBidPro.Domain.Enums;

namespace GovBidPro.Infrastructure.SamGov;

/// <summary>
/// Clearly labeled demo/fixture provider (handoff §6.2). Its notices are fictional, use reserved
/// example.invalid URLs, and are stored with IsLiveSource = false so the UI labels them as demo data.
/// They must never be presented as live government data.
/// </summary>
public sealed class FixtureOpportunitySource : IOpportunitySource
{
    public const string Key = "fixture";
    private const int PageSize = 5;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Lazy<IReadOnlyList<ImportedOpportunity>> _items = new(Load);

    public string SourceKey => Key;

    public bool IsLive => false;

    public Task<OpportunityImportPage> FetchAsync(OpportunityImportCursor? cursor, CancellationToken cancellationToken)
    {
        var offset = cursor?.Value is { } value ? int.Parse(value, CultureInfo.InvariantCulture) : 0;
        var items = _items.Value.Skip(offset).Take(PageSize).ToList();
        var next = offset + PageSize < _items.Value.Count ? new OpportunityImportCursor((offset + PageSize).ToString(CultureInfo.InvariantCulture)) : null;
        return Task.FromResult(new OpportunityImportPage(items, next));
    }

    private static List<ImportedOpportunity> Load()
    {
        using var stream = typeof(FixtureOpportunitySource).Assembly.GetManifestResourceStream("GovBidPro.Infrastructure.SamGov.Fixtures.demo-notices.json")
            ?? throw new InvalidOperationException("Fixture resource missing.");
        var notices = JsonSerializer.Deserialize<List<FixtureNotice>>(stream, JsonOptions) ?? [];
        return notices.Select(n => new ImportedOpportunity
        {
            ExternalId = n.Id,
            Title = n.Title,
            SolicitationNumber = n.SolicitationNumber,
            Description = n.Description,
            Agency = n.Agency,
            Office = n.Office,
            NoticeType = Enum.TryParse<SourceNoticeType>(n.NoticeType, out var t) ? t : SourceNoticeType.Unknown,
            SourceNoticeTypeRaw = n.NoticeTypeLabel,
            NaicsCode = n.Naics,
            PscCode = n.Psc,
            SetAside = n.SetAside,
            PlaceOfPerformanceState = n.State,
            PlaceOfPerformanceCountry = "USA",
            PostedAt = n.PostedAt,
            SourceUpdatedAt = n.PostedAt,
            ResponseDueAt = n.ResponseDueAt,
            ResponseDueOriginalText = n.ResponseDueText,
            ResponseDueTimeZone = n.ResponseDueTimeZone,
            SubmissionInstructions = n.SubmissionInstructions,
            SourceUrl = $"https://example.invalid/fixture/{n.Id}",
        }).ToList();
    }

    private sealed record FixtureNotice(
        string Id,
        string Title,
        string? SolicitationNumber,
        string? Description,
        string? Agency,
        string? Office,
        string NoticeType,
        string? NoticeTypeLabel,
        string? Naics,
        string? Psc,
        string? SetAside,
        string? State,
        DateTimeOffset? PostedAt,
        DateTimeOffset? ResponseDueAt,
        string? ResponseDueText,
        string? ResponseDueTimeZone,
        string? SubmissionInstructions);
}
