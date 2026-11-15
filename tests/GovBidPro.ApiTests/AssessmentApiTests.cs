using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovBidPro.Testing;

namespace GovBidPro.ApiTests;

/// <summary>P1.3 over HTTP: profile, fit preview, Go/No-go recording, validation and tenant scoping.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class AssessmentApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private GovBidProFactory _factory = null!;

    public async Task InitializeAsync() => _factory = new GovBidProFactory(await postgres.CreateEmptyDatabaseAsync());

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static async Task<string> FirstOpportunityIdAsync(HttpClient client)
    {
        using var list = JsonDocument.Parse(await client.GetStringAsync("/api/v1/opportunities?keywords=cloud&pageSize=1"));
        return list.RootElement.GetProperty("results").GetProperty("items")[0].GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Company_admin_can_set_a_profile_score_an_opportunity_and_record_a_decision()
    {
        using var client = await _factory.CreateCompanyUserClientAsync("Scoring Co");
        var id = await FirstOpportunityIdAsync(client);

        var profile = await client.PutAsJsonAsync("/api/v1/company/profile", new { naicsCodes = new[] { "541511" }, setAsideQualifications = new[] { "SmallBusiness" }, capabilityKeywords = new[] { "cloud" }, preferredStates = new[] { "VA" }, preferredAgencies = Array.Empty<string>(), minimumResponseDays = 0 });
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/company/profile", new { naicsCodes = new[] { "abc" } })).StatusCode);

        using var fit = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/opportunities/{id}/fit"));
        Assert.Equal("fit-v1", fit.RootElement.GetProperty("ruleVersion").GetString());
        Assert.Equal(6, fit.RootElement.GetProperty("criteria").GetArrayLength());
        Assert.Equal("Met", fit.RootElement.GetProperty("criteria")[0].GetProperty("outcome").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/opportunities/{id}/assessments", new { decision = "Go" })).StatusCode);
        var recorded = await client.PostAsJsonAsync($"/api/v1/opportunities/{id}/assessments", new { decision = "Go", rationale = "Matches our core NAICS." });
        Assert.Equal(HttpStatusCode.Created, recorded.StatusCode);

        using var history = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/opportunities/{id}/assessments"));
        Assert.Equal("Go", history.RootElement[0].GetProperty("decision").GetString());
        using var latest = JsonDocument.Parse(await client.GetStringAsync("/api/v1/assessments?decision=Go"));
        Assert.Equal(id, latest.RootElement[0].GetProperty("opportunityId").GetString());
        using var dashboard = JsonDocument.Parse(await client.GetStringAsync("/api/v1/dashboard"));
        Assert.Equal(1, dashboard.RootElement.GetProperty("decisions").GetProperty("go").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Company")).StatusCode);
    }

    [Fact]
    public async Task Assessments_are_private_to_the_company_and_unknown_opportunities_are_404()
    {
        using var a = await _factory.CreateCompanyUserClientAsync("Assess A");
        using var b = await _factory.CreateCompanyUserClientAsync("Assess B");
        var id = await FirstOpportunityIdAsync(a);
        await a.PostAsJsonAsync($"/api/v1/opportunities/{id}/assessments", new { decision = "NoGo", rationale = "Not for us." });

        Assert.Equal("[]", await b.GetStringAsync($"/api/v1/opportunities/{id}/assessments"));
        Assert.Equal("[]", await b.GetStringAsync("/api/v1/assessments"));
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/v1/opportunities/{Guid.NewGuid()}/fit")).StatusCode);

        using var anonymous = _factory.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/company/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/assessments")).StatusCode);
    }
}
