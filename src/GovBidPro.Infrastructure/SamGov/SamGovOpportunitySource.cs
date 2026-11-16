using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using GovBidPro.Application.Opportunities;
using GovBidPro.Domain.Enums;
using Microsoft.Extensions.Options;

namespace GovBidPro.Infrastructure.SamGov;

/// <summary>
/// Status of the official SAM.gov integration. The adapter below follows the published
/// "Get Opportunities Public API" v2 documentation (https://open.gsa.gov/api/get-opportunities-public-api/,
/// read 2026-11-15) but has not been exercised against the live service because no API key has been
/// issued yet. Until an operator completes the checklist in docs/operations/sam-gov-sync.md the product
/// keeps saying so, and the fixture source remains the default.
/// </summary>
public static class SamGovIntegrationStatus
{
    public const string SourceKey = "sam.gov";

    public const bool LiveIntegrationVerified = false;

    public const string Note = "The SAM.gov adapter is built from the official API documentation but has not been verified against the live service (no API key yet). Fixture data is the default.";
}

public sealed class SamGovOptions
{
    public const string SectionName = "OpportunitySources:SamGov";

    /// <summary>Off by default. Turning it on requires <see cref="ApiKey"/>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Public API key from the SAM.gov account details page. A secret: supply it through the environment, never in a file.</summary>
    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://api.sam.gov/opportunities/v2/search";

    /// <summary>Records per request. The API allows at most 1000.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>How many days back each sync window starts. Overlap is harmless: imports are idempotent.</summary>
    public int LookbackDays { get; set; } = 2;

    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// How the API's "offset" parameter advances. The documentation calls it a page index, so that is the
    /// default; "RecordOffset" is available if live verification shows it counts records instead.
    /// </summary>
    public string OffsetMode { get; set; } = "PageIndex";

    public static bool IsValid(SamGovOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        if (!o.Enabled)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(o.ApiKey)
            && Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && o.PageSize is >= 1 and <= 1000
            && o.LookbackDays is >= 1 and <= 364
            && o.RequestTimeoutSeconds is >= 5 and <= 120
            && o.OffsetMode is "PageIndex" or "RecordOffset";
    }
}

/// <summary>
/// Official SAM.gov "Get Opportunities Public API" v2 adapter (handoff §6.1, milestone P1.2).
/// Uses only the documented API with the operator's own key: no scraping, no access-control evasion.
/// Each call fetches one bounded page; the cursor carries the date window and offset so an interrupted
/// run resumes where it stopped. The API key is sent as the documented <c>api_key</c> query parameter,
/// so request logging is removed for this client and no error message ever contains the request URL.
/// </summary>
public sealed partial class SamGovOpportunitySource(HttpClient http, IOptions<SamGovOptions> options, TimeProvider clock) : IOpportunitySource
{
    public const string HttpClientName = "sam.gov";
    private const string DateFormat = "MM/dd/yyyy";

    public string SourceKey => SamGovIntegrationStatus.SourceKey;

    public bool IsLive => true;

    public async Task<OpportunityImportPage> FetchAsync(OpportunityImportCursor? cursor, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new OpportunitySourceException("SAM.gov sync is not enabled or has no API key.");
        }

        var window = ParseCursor(cursor) ?? NewWindow(settings);
        var uri = $"{settings.BaseUrl}?api_key={Uri.EscapeDataString(settings.ApiKey)}&postedFrom={Uri.EscapeDataString(window.From)}&postedTo={Uri.EscapeDataString(window.To)}&limit={settings.PageSize.ToString(CultureInfo.InvariantCulture)}&offset={window.Offset.ToString(CultureInfo.InvariantCulture)}";

        HttpStatusCode status;
        TimeSpan? retryAfter;
        byte[] body;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("application/json");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
            status = response.StatusCode;
            retryAfter = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - clock.GetUtcNow() : null);
            body = await response.Content.ReadAsByteArrayAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OpportunitySourceException("SAM.gov request timed out.");
        }
        catch (HttpRequestException ex)
        {
            // The inner exception is deliberately dropped: it can carry the request URL, which contains the API key.
            throw new OpportunitySourceException($"SAM.gov request failed ({ex.HttpRequestError}).");
        }

        switch (status)
        {
            case HttpStatusCode.TooManyRequests:
                throw new OpportunitySourceRateLimitedException(retryAfter is { } delay && delay > TimeSpan.Zero ? delay : TimeSpan.FromHours(1));
            case HttpStatusCode.NotFound:
                // Documented as "No Data found" for an empty window.
                return new OpportunityImportPage([], null);
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                throw new OpportunitySourceException($"SAM.gov rejected the API key or access (HTTP {(int)status}).");
            case not HttpStatusCode.OK:
                throw new OpportunitySourceException($"SAM.gov returned HTTP {(int)status}.");
        }

        return Parse(body, window, settings);
    }

    /// <summary>Maps the documented <c>type</c> text to the internal notice vocabulary. The verbatim value is always kept.</summary>
    public static SourceNoticeType MapNoticeType(string? type)
    {
        var normalized = (type ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        return normalized switch
        {
            "" => SourceNoticeType.Unknown,
            "PRESOLICITATION" => SourceNoticeType.PreSolicitation,
            "SOLICITATION" => SourceNoticeType.Solicitation,
            "COMBINEDSYNOPSIS/SOLICITATION" => SourceNoticeType.CombinedSynopsisSolicitation,
            "SOURCESSOUGHT" => SourceNoticeType.SourcesSought,
            "SPECIALNOTICE" => SourceNoticeType.SpecialNotice,
            "AWARDNOTICE" => SourceNoticeType.AwardNotice,
            _ => SourceNoticeType.Other,
        };
    }

    private static Window NewWindow(SamGovOptions settings, DateTimeOffset now)
    {
        var to = now.UtcDateTime.Date;
        return new Window(to.AddDays(-settings.LookbackDays).ToString(DateFormat, CultureInfo.InvariantCulture), to.ToString(DateFormat, CultureInfo.InvariantCulture), 0);
    }

    private static Window? ParseCursor(OpportunityImportCursor? cursor)
    {
        var parts = cursor?.Value?.Split('|');
        if (parts is not { Length: 3 } || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var offset))
        {
            return null;
        }

        return DateTime.TryParseExact(parts[0], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            && DateTime.TryParseExact(parts[1], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? new Window(parts[0], parts[1], offset)
            : null;
    }

    private static OpportunityImportPage Parse(byte[] body, Window window, SamGovOptions settings)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new OpportunitySourceException("SAM.gov returned a response that is not valid JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("opportunitiesData", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                throw new OpportunitySourceException("SAM.gov response did not contain the documented opportunitiesData array.");
            }

            var items = new List<ImportedOpportunity>(data.GetArrayLength());
            foreach (var element in data.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    items.Add(Map(element));
                }
            }

            var total = root.TryGetProperty("totalRecords", out var totalElement) && totalElement.TryGetInt32(out var parsedTotal) ? parsedTotal : 0;
            var recordOffset = settings.OffsetMode == "RecordOffset";
            var consumed = recordOffset ? window.Offset + items.Count : (long)(window.Offset + 1) * settings.PageSize;
            OpportunityImportCursor? next = null;
            if (items.Count > 0 && consumed < total)
            {
                var nextOffset = recordOffset ? window.Offset + items.Count : window.Offset + 1;
                next = new OpportunityImportCursor($"{window.From}|{window.To}|{nextOffset.ToString(CultureInfo.InvariantCulture)}");
            }

            return new OpportunityImportPage(items, next);
        }
    }

    private static ImportedOpportunity Map(JsonElement o)
    {
        var path = Text(o, "fullParentPathName");
        var segments = path?.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        var noticeId = Text(o, "noticeId") ?? string.Empty;
        var type = Text(o, "type");
        var deadlineText = Text(o, "responseDeadLine");
        var (dueAt, zone) = ParseDeadline(deadlineText);
        var place = o.TryGetProperty("placeOfPerformance", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;

        return new ImportedOpportunity
        {
            ExternalId = noticeId,
            Title = Text(o, "title"),
            SolicitationNumber = Text(o, "solicitationNumber"),

            // The API returns a link to the description that needs a second keyed request per notice.
            // It is not fetched (daily request quota); users follow the source link instead.
            Description = null,
            Agency = segments.Length > 0 ? segments[0] : null,
            Office = segments.Length > 1 ? segments[^1] : null,
            NoticeType = MapNoticeType(type),
            SourceNoticeTypeRaw = type,
            NaicsCode = Text(o, "naicsCode"),
            PscCode = Text(o, "classificationCode"),
            SetAside = Text(o, "typeOfSetAsideDescription") ?? Text(o, "typeOfSetAside"),
            PlaceOfPerformanceState = Nested(place, "state", "code"),
            PlaceOfPerformanceCountry = Nested(place, "country", "code"),
            PostedAt = ParsePostedDate(Text(o, "postedDate")),
            SourceUpdatedAt = null,
            ResponseDueAt = dueAt,
            ResponseDueOriginalText = deadlineText,
            ResponseDueTimeZone = zone,
            SubmissionInstructions = null,
            SourceUrl = SafeSourceUrl(Text(o, "uiLink"), noticeId),
        };
    }

    /// <summary>
    /// A deadline becomes an instant only when the source states a UTC offset. Otherwise only the
    /// original text is kept, because guessing a time zone could show a wrong deadline.
    /// </summary>
    private static (DateTimeOffset? DueAt, string? Zone) ParseDeadline(string? text)
    {
        if (text is null || !HasOffset().IsMatch(text) || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return (null, null);
        }

        var offset = parsed.Offset;
        var zone = offset == TimeSpan.Zero ? "UTC" : $"UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration():hh\\:mm}";
        return (parsed.ToUniversalTime(), zone);
    }

    /// <summary>Posted dates are published without a time zone; they are recorded as UTC calendar dates.</summary>
    private static DateTimeOffset? ParsePostedDate(string? text) =>
        text is not null && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? new DateTimeOffset(parsed, TimeSpan.Zero)
            : null;

    private static string? SafeSourceUrl(string? uiLink, string noticeId)
    {
        if (Uri.TryCreate(uiLink, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && (uri.Host == "sam.gov" || uri.Host.EndsWith(".sam.gov", StringComparison.OrdinalIgnoreCase)))
        {
            return uri.ToString();
        }

        return NoticeId().IsMatch(noticeId) ? $"https://sam.gov/opp/{noticeId}/view" : null;
    }

    private static string? Text(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string? Nested(JsonElement element, string child, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(child, out var inner) ? Text(inner, name) : null;

    private Window NewWindow(SamGovOptions settings) => NewWindow(settings, clock.GetUtcNow());

    [GeneratedRegex(@"(Z|[+-]\d{2}:?\d{2})$")]
    private static partial Regex HasOffset();

    [GeneratedRegex("^[A-Za-z0-9]{1,64}$")]
    private static partial Regex NoticeId();

    private sealed record Window(string From, string To, int Offset);
}
