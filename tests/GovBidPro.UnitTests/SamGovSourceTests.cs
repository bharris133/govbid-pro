using System.Net;
using System.Text;
using GovBidPro.Application.Opportunities;
using GovBidPro.Domain.Enums;
using GovBidPro.Infrastructure.SamGov;
using Microsoft.Extensions.Options;

namespace GovBidPro.UnitTests;

/// <summary>
/// P1.2: the SAM.gov adapter against a fake HTTP handler shaped like the documented v2 response.
/// No network is used; the live service still needs operator verification with a real key.
/// </summary>
public sealed class SamGovSourceTests
{
    private const string ApiKey = "test-key-NEVER-LOGGED";
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 9, 30, 0, TimeSpan.Zero);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (SamGovOpportunitySource Source, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond, Action<SamGovOptions>? configure = null)
    {
        var options = new SamGovOptions { Enabled = true, ApiKey = ApiKey, PageSize = 2 };
        configure?.Invoke(options);
        var handler = new StubHandler(respond);
        return (new SamGovOpportunitySource(new HttpClient(handler), Options.Create(options), new FixedClock()), handler);
    }

    private const string TwoNotices = """
        {
          "totalRecords": 3, "limit": 2, "offset": 0,
          "opportunitiesData": [
            {
              "noticeId": "abc123def456", "title": "Cloud Hosting Services", "solicitationNumber": "W91-26-R-0001",
              "fullParentPathName": "DEPT OF DEFENSE.DEPT OF THE ARMY.W6QK ACC-APG", "postedDate": "2026-11-14",
              "type": "Combined Synopsis/Solicitation", "baseType": "Presolicitation",
              "typeOfSetAside": "SBA", "typeOfSetAsideDescription": "Total Small Business Set-Aside (FAR 19.5)",
              "responseDeadLine": "2026-12-01T16:00:00-05:00", "naicsCode": "541512", "classificationCode": "DA01",
              "active": "Yes", "description": "https://api.sam.gov/prod/opportunities/v1/noticedesc?noticeid=abc123def456",
              "placeOfPerformance": { "city": { "code": "1", "name": "Aberdeen" }, "state": { "code": "MD", "name": "Maryland" }, "country": { "code": "USA", "name": "UNITED STATES" } },
              "uiLink": "https://sam.gov/opp/abc123def456/view"
            },
            {
              "noticeId": "zzz999", "title": "Market research", "type": "Sources Sought", "postedDate": "2026-11-13 08:15:00",
              "responseDeadLine": "2026-11-30T17:00:00", "naicsCode": 561210, "placeOfPerformance": null, "uiLink": "javascript:alert(1)"
            }
          ]
        }
        """;

    [Fact]
    public async Task Maps_documented_fields_and_keeps_notice_type_separate_from_raw_value()
    {
        var (source, handler) = Create(_ => Json(TwoNotices));
        var page = await source.FetchAsync(null, CancellationToken.None);

        Assert.True(source.IsLive);
        Assert.Equal("sam.gov", source.SourceKey);
        var first = page.Items[0];
        Assert.Equal("abc123def456", first.ExternalId);
        Assert.Equal(SourceNoticeType.CombinedSynopsisSolicitation, first.NoticeType);
        Assert.Equal("Combined Synopsis/Solicitation", first.SourceNoticeTypeRaw);
        Assert.Equal("DEPT OF DEFENSE", first.Agency);
        Assert.Equal("W6QK ACC-APG", first.Office);
        Assert.Equal("541512", first.NaicsCode);
        Assert.Equal("DA01", first.PscCode);
        Assert.Equal("Total Small Business Set-Aside (FAR 19.5)", first.SetAside);
        Assert.Equal("MD", first.PlaceOfPerformanceState);
        Assert.Equal("USA", first.PlaceOfPerformanceCountry);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero), first.PostedAt);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 21, 0, 0, TimeSpan.Zero), first.ResponseDueAt);
        Assert.Equal("2026-12-01T16:00:00-05:00", first.ResponseDueOriginalText);
        Assert.Equal("UTC-05:00", first.ResponseDueTimeZone);
        Assert.Equal("https://sam.gov/opp/abc123def456/view", first.SourceUrl);
        Assert.Null(first.Description);

        // The request used the documented parameters and a window ending today.
        var query = handler.Requests[0].Query;
        Assert.Contains("postedFrom=11%2F13%2F2026", query, StringComparison.Ordinal);
        Assert.Contains("postedTo=11%2F15%2F2026", query, StringComparison.Ordinal);
        Assert.Contains("limit=2&offset=0", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deadline_without_a_utc_offset_is_kept_as_text_only_and_unsafe_links_are_replaced()
    {
        var (source, _) = Create(_ => Json(TwoNotices));
        var second = (await source.FetchAsync(null, CancellationToken.None)).Items[1];
        Assert.Null(second.ResponseDueAt);
        Assert.Equal("2026-11-30T17:00:00", second.ResponseDueOriginalText);
        Assert.Equal(SourceNoticeType.SourcesSought, second.NoticeType);
        Assert.Equal("561210", second.NaicsCode);
        Assert.Null(second.PlaceOfPerformanceState);
        Assert.Equal("https://sam.gov/opp/zzz999/view", second.SourceUrl);
    }

    [Fact]
    public async Task Cursor_carries_the_window_and_advances_until_all_records_are_read()
    {
        var (source, handler) = Create(request => request.RequestUri!.Query.Contains("offset=1", StringComparison.Ordinal)
            ? Json("""{ "totalRecords": 3, "limit": 2, "offset": 1, "opportunitiesData": [ { "noticeId": "last1", "title": "Third" } ] }""")
            : Json(TwoNotices));

        var first = await source.FetchAsync(null, CancellationToken.None);
        Assert.Equal("11/13/2026|11/15/2026|1", first.NextCursor?.Value);
        var second = await source.FetchAsync(first.NextCursor, CancellationToken.None);
        Assert.Null(second.NextCursor);
        Assert.Equal("last1", Assert.Single(second.Items).ExternalId);
        Assert.Contains("postedFrom=11%2F13%2F2026", handler.Requests[1].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Record_offset_mode_advances_by_records_returned()
    {
        var (source, _) = Create(_ => Json(TwoNotices), o => o.OffsetMode = "RecordOffset");
        Assert.Equal("11/13/2026|11/15/2026|2", (await source.FetchAsync(null, CancellationToken.None)).NextCursor?.Value);
    }

    [Fact]
    public async Task Rate_limit_is_reported_with_the_retry_delay()
    {
        var (source, _) = Create(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(90));
            return response;
        });
        var ex = await Assert.ThrowsAsync<OpportunitySourceRateLimitedException>(() => source.FetchAsync(null, CancellationToken.None));
        Assert.Equal(TimeSpan.FromMinutes(90), ex.RetryAfter);
    }

    [Fact]
    public async Task No_data_404_is_an_empty_final_page()
    {
        var (source, _) = Create(_ => Json("""{ "error": "No Data found" }""", HttpStatusCode.NotFound));
        var page = await source.FetchAsync(null, CancellationToken.None);
        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "{}")]
    [InlineData(HttpStatusCode.InternalServerError, "oops")]
    [InlineData(HttpStatusCode.OK, "<html>not json</html>")]
    [InlineData(HttpStatusCode.OK, """{ "unexpected": true }""")]
    public async Task Failures_raise_safe_errors_that_never_contain_the_api_key(HttpStatusCode status, string body)
    {
        var (source, _) = Create(_ => Json(body, status));
        var ex = await Assert.ThrowsAsync<OpportunitySourceException>(() => source.FetchAsync(null, CancellationToken.None));
        Assert.IsNotType<OpportunitySourceRateLimitedException>(ex);
        Assert.DoesNotContain(ApiKey, ex.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("api_key", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Network_failure_does_not_leak_the_request_url()
    {
        var (source, _) = Create(request => throw new HttpRequestException($"Connection refused ({request.RequestUri})"));
        var ex = await Assert.ThrowsAsync<OpportunitySourceException>(() => source.FetchAsync(null, CancellationToken.None));
        Assert.DoesNotContain(ApiKey, ex.ToString(), StringComparison.Ordinal);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task Disabled_or_keyless_source_refuses_to_call_out()
    {
        var (source, handler) = Create(_ => Json(TwoNotices), o => o.ApiKey = null);
        await Assert.ThrowsAsync<OpportunitySourceException>(() => source.FetchAsync(null, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("Presolicitation", SourceNoticeType.PreSolicitation)]
    [InlineData("Solicitation", SourceNoticeType.Solicitation)]
    [InlineData("Award Notice", SourceNoticeType.AwardNotice)]
    [InlineData("Special Notice", SourceNoticeType.SpecialNotice)]
    [InlineData("Justification", SourceNoticeType.Other)]
    [InlineData("Sale of Surplus Property", SourceNoticeType.Other)]
    [InlineData(null, SourceNoticeType.Unknown)]
    public void Notice_type_mapping_is_explicit(string? raw, SourceNoticeType expected) =>
        Assert.Equal(expected, SamGovOpportunitySource.MapNoticeType(raw));

    [Fact]
    public void Options_are_valid_when_disabled_and_strict_when_enabled()
    {
        Assert.True(SamGovOptions.IsValid(new SamGovOptions()));
        Assert.False(SamGovOptions.IsValid(new SamGovOptions { Enabled = true }));
        Assert.False(SamGovOptions.IsValid(new SamGovOptions { Enabled = true, ApiKey = "k", BaseUrl = "http://api.sam.gov/x" }));
        Assert.False(SamGovOptions.IsValid(new SamGovOptions { Enabled = true, ApiKey = "k", PageSize = 1001 }));
        Assert.True(SamGovOptions.IsValid(new SamGovOptions { Enabled = true, ApiKey = "k" }));
    }
}
