using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GovBidPro.Infrastructure.Identity;
using GovBidPro.Testing;

namespace GovBidPro.ApiTests;

/// <summary>API-level security acceptance: 401/403/404, CSRF, cross-tenant access, uploads, admin MFA, headers.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class ApiSecurityTests(PostgresFixture postgres) : IAsyncLifetime
{
    private GovBidProFactory _factory = null!;

    public async Task InitializeAsync() => _factory = new GovBidProFactory(await postgres.CreateEmptyDatabaseAsync());

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Health_endpoints_are_public_and_reveal_nothing_sensitive()
    {
        using var client = _factory.CreateBrowserClient();
        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/v1/opportunities")]
    [InlineData("/api/v1/saved-searches")]
    [InlineData("/api/v1/documents")]
    [InlineData("/api/v1/me")]
    public async Task Unauthenticated_api_requests_get_401(string path)
    {
        using var client = _factory.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Signed_in_user_without_company_gets_403_from_tenant_apis()
    {
        using var client = _factory.CreateBrowserClient();
        await GovBidProFactory.SignInAsync(client, await _factory.CreateUserAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/opportunities")).StatusCode);
    }

    [Fact]
    public async Task Opportunity_list_labels_fixture_data_and_paginates()
    {
        using var client = await _factory.CreateCompanyUserClientAsync("Search Co");
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/v1/opportunities?pageSize=5&page=2"));
        var results = doc.RootElement.GetProperty("results");
        Assert.Equal(12, results.GetProperty("totalCount").GetInt32());
        Assert.Equal(5, results.GetProperty("items").GetArrayLength());
        Assert.All(results.GetProperty("items").EnumerateArray(), i => Assert.False(i.GetProperty("isLiveSource").GetBoolean()));
        var source = Assert.Single(doc.RootElement.GetProperty("sources").EnumerateArray());
        Assert.Equal("fixture", source.GetProperty("sourceKey").GetString());
        Assert.False(source.GetProperty("isLiveSource").GetBoolean());

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/opportunities/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Mutations_without_antiforgery_token_are_rejected()
    {
        using var client = await _factory.CreateCompanyUserClientAsync("Csrf Co");
        using var noHeader = new HttpRequestMessage(HttpMethod.Post, "/api/v1/saved-searches") { Content = JsonContent.Create(new { name = "x", criteria = new { }, notificationFrequency = "None" }) };
        noHeader.Headers.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(noHeader)).StatusCode);

        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "forged-token");
        var forged = await client.PostAsJsonAsync("/api/v1/saved-searches", new { name = "x", criteria = new { }, notificationFrequency = "None" });
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
    }

    [Fact]
    public async Task Tenant_b_cannot_read_or_delete_tenant_a_saved_search()
    {
        using var a = await _factory.CreateCompanyUserClientAsync("Tenant A");
        using var b = await _factory.CreateCompanyUserClientAsync("Tenant B");

        var created = await a.PostAsJsonAsync("/api/v1/saved-searches", new { name = "A only", criteria = new { keywords = "cloud", naicsCodes = new[] { "541511" } }, notificationFrequency = "None" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/v1/saved-searches/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/v1/saved-searches/{id}")).StatusCode);
        Assert.Equal("[]", await b.GetStringAsync("/api/v1/saved-searches"));
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync($"/api/v1/saved-searches/{id}")).StatusCode);
    }

    [Fact]
    public async Task Uploads_with_markings_are_rejected_and_documents_are_tenant_scoped()
    {
        using var a = await _factory.CreateCompanyUserClientAsync("Docs A");
        using var b = await _factory.CreateCompanyUserClientAsync("Docs B");

        var rejected = await a.PostAsync("/api/v1/documents", Upload("CUI//SP-PRVCY\nexample"));
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Contains("upload.suspected_restricted_content.marker.cui", await rejected.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var accepted = await a.PostAsync("/api/v1/documents", Upload("Ordinary pricing notes."));
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        using var body = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetString();

        var download = await a.GetAsync($"/api/v1/documents/{id}/content");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("no-store", download.Headers.CacheControl?.ToString());
        Assert.Equal("Ordinary pricing notes.", await download.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/v1/documents/{id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/v1/documents/{id}")).StatusCode);
        Assert.Equal("[]", await b.GetStringAsync("/api/v1/documents"));
    }

    [Fact]
    public async Task Platform_admin_endpoints_require_role_and_mfa()
    {
        using var regular = await _factory.CreateCompanyUserClientAsync("Regular Co");
        Assert.Equal(HttpStatusCode.Forbidden, (await regular.GetAsync("/api/v1/admin/imports")).StatusCode);

        using var adminWithoutMfa = _factory.CreateBrowserClient();
        await GovBidProFactory.SignInAsync(adminWithoutMfa, await _factory.CreateUserAsync(PlatformRoles.PlatformAdmin));
        Assert.Equal(HttpStatusCode.Forbidden, (await adminWithoutMfa.GetAsync("/api/v1/admin/imports")).StatusCode);
    }

    [Fact]
    public async Task Pages_send_security_headers_and_no_inline_script()
    {
        using var client = _factory.CreateBrowserClient();
        var response = await client.GetAsync("/");
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", csp, StringComparison.Ordinal);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("type=\"module\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("onclick=", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Me_reports_ai_off_and_sam_gov_not_verified()
    {
        using var client = await _factory.CreateCompanyUserClientAsync("Me Co");
        using var me = JsonDocument.Parse(await client.GetStringAsync("/api/v1/me"));
        Assert.False(me.RootElement.GetProperty("aiPlatformEnabled").GetBoolean());
        Assert.False(me.RootElement.GetProperty("aiEnabledForTenant").GetBoolean());
        Assert.False(me.RootElement.GetProperty("samGovLiveIntegrationVerified").GetBoolean());
        Assert.Equal("CompanyAdmin", me.RootElement.GetProperty("role").GetString());
    }

    private static MultipartFormDataContent Upload(string text)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        return new MultipartFormDataContent
        {
            { file, "file", "notes.txt" },
            { new StringContent("ConfidentialCommercial"), "classification" },
            { new StringContent("true"), "affirmPermittedToUpload" },
            { new StringContent("true"), "affirmNoProhibitedContent" },
        };
    }
}
