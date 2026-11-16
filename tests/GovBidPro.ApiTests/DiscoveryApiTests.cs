using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovBidPro.Testing;

namespace GovBidPro.ApiTests;

/// <summary>P1.1: dashboard, sorting, opportunity detail and saved-search updates over HTTP.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class DiscoveryApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private GovBidProFactory _factory = null!;

    public async Task InitializeAsync() => _factory = new GovBidProFactory(await postgres.CreateEmptyDatabaseAsync());

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Dashboard_requires_a_company_and_returns_labeled_fixture_sources()
    {
        using var anonymous = _factory.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/dashboard")).StatusCode);

        using var client = await _factory.CreateCompanyUserClientAsync("Dash Co");
        await client.PostAsJsonAsync("/api/v1/saved-searches", new { name = "Everything", criteria = new { }, notificationFrequency = "Weekly" });
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/v1/dashboard"));
        Assert.True(doc.RootElement.GetProperty("discovery").GetProperty("newLast7Days").GetInt32() >= 12);
        Assert.Equal("Everything", doc.RootElement.GetProperty("savedSearches")[0].GetProperty("name").GetString());
        Assert.False(doc.RootElement.GetProperty("sources")[0].GetProperty("isLiveSource").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Opportunities/Detail?id=" + Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Opportunity_detail_exposes_provenance_and_search_sorts_by_deadline()
    {
        using var client = await _factory.CreateCompanyUserClientAsync("Detail Co");
        using var list = JsonDocument.Parse(await client.GetStringAsync("/api/v1/opportunities?sort=DueSoonest&pageSize=100"));
        var items = list.RootElement.GetProperty("results").GetProperty("items").EnumerateArray().ToList();
        var due = items.Where(i => i.GetProperty("responseDueAt").ValueKind != JsonValueKind.Null).Select(i => i.GetProperty("responseDueAt").GetDateTimeOffset()).ToList();
        Assert.Equal(due.OrderBy(d => d), due);

        using var detail = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/opportunities/{items[0].GetProperty("id").GetString()}"));
        Assert.Equal("fixture", detail.RootElement.GetProperty("sourceKey").GetString());
        Assert.StartsWith("https://example.invalid/", detail.RootElement.GetProperty("sourceUrl").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, detail.RootElement.GetProperty("revisions").GetArrayLength());
    }

    [Fact]
    public async Task Saved_search_update_is_tenant_scoped()
    {
        using var a = await _factory.CreateCompanyUserClientAsync("Update A");
        using var b = await _factory.CreateCompanyUserClientAsync("Update B");
        var created = await a.PostAsJsonAsync("/api/v1/saved-searches", new { name = "Mine", criteria = new { keywords = "cloud" }, notificationFrequency = "None" });
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetString();
        var update = new { name = "Renamed", criteria = new { keywords = "cloud", openOnly = true }, notificationFrequency = "Daily" };

        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"/api/v1/saved-searches/{id}", update)).StatusCode);
        var ok = await a.PutAsJsonAsync($"/api/v1/saved-searches/{id}", update);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using var updated = JsonDocument.Parse(await ok.Content.ReadAsStringAsync());
        Assert.Equal("Daily", updated.RootElement.GetProperty("notificationFrequency").GetString());
        Assert.True(updated.RootElement.GetProperty("criteria").GetProperty("openOnly").GetBoolean());
    }
}
