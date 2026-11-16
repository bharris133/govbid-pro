using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GovBidPro.Testing;

namespace GovBidPro.ApiTests;

/// <summary>P1.5 over HTTP: versions, templates, proposal links and exports.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class DocumentApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private GovBidProFactory _factory = null!;

    public async Task InitializeAsync() => _factory = new GovBidProFactory(await postgres.CreateEmptyDatabaseAsync());

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static MultipartFormDataContent Form(string text, string fileName, params (string Name, string Value)[] fields)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var form = new MultipartFormDataContent { { file, "file", fileName }, { new StringContent("true"), "affirmPermittedToUpload" }, { new StringContent("true"), "affirmNoProhibitedContent" } };
        foreach (var (name, value) in fields)
        {
            form.Add(new StringContent(value), name);
        }

        return form;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task Versions_templates_and_exports_work_end_to_end()
    {
        using var client = await _factory.CreateCompanyUserClientAsync("Docs Co");
        using var list = JsonDocument.Parse(await client.GetStringAsync("/api/v1/opportunities?pageSize=1"));
        var opportunityId = list.RootElement.GetProperty("results").GetProperty("items")[0].GetProperty("id").GetString();
        var proposalId = (await JsonAsync(await client.PostAsJsonAsync("/api/v1/proposals", new { opportunityId }))).GetProperty("id").GetString();

        var uploaded = await client.PostAsync("/api/v1/documents", Form("Boilerplate v1", "boilerplate.txt", ("classification", "BusinessInternal"), ("isTemplate", "true")));
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var template = await JsonAsync(uploaded);
        var templateId = template.GetProperty("id").GetString();
        Assert.True(template.GetProperty("isTemplate").GetBoolean());
        Assert.Equal(1, template.GetProperty("currentVersion").GetInt32());

        var version = await client.PostAsync($"/api/v1/documents/{templateId}/versions", Form("Boilerplate v2", "boilerplate.txt", ("note", "Refreshed")));
        Assert.Equal(HttpStatusCode.Created, version.StatusCode);
        Assert.Equal(2, (await JsonAsync(version)).GetProperty("currentVersion").GetInt32());
        var rejected = await client.PostAsync($"/api/v1/documents/{templateId}/versions", Form("CUI//SP-PRVCY\nx", "boilerplate.txt"));
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);

        using var versions = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/documents/{templateId}/versions"));
        Assert.Equal(2, versions.RootElement.GetArrayLength());
        Assert.Equal("Refreshed", versions.RootElement[0].GetProperty("note").GetString());
        var old = await client.GetAsync($"/api/v1/documents/{templateId}/versions/1/content");
        Assert.Equal("Boilerplate v1", await old.Content.ReadAsStringAsync());
        Assert.True(old.Headers.CacheControl?.NoStore);
        Assert.Equal("Boilerplate v2", await client.GetStringAsync($"/api/v1/documents/{templateId}/content"));

        var copied = await client.PostAsJsonAsync($"/api/v1/documents/{templateId}/copy", new { proposalId });
        Assert.Equal(HttpStatusCode.Created, copied.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/documents/{templateId}/copy", new { proposalId = Guid.NewGuid() })).StatusCode);
        using var linked = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/documents?proposalId={proposalId}"));
        Assert.Equal(1, linked.RootElement.GetArrayLength());
        Assert.False(linked.RootElement[0].GetProperty("isTemplate").GetBoolean());
        using var templates = JsonDocument.Parse(await client.GetStringAsync("/api/v1/documents?templates=true"));
        Assert.Equal(templateId, templates.RootElement[0].GetProperty("id").GetString());

        var placed = await client.PutAsJsonAsync($"/api/v1/documents/{templateId}/placement", new { proposalId = (string?)null, isTemplate = false });
        Assert.False((await JsonAsync(placed)).GetProperty("isTemplate").GetBoolean());

        await client.PostAsJsonAsync($"/api/v1/proposals/{proposalId}/requirements", new { reference = "L.1", text = "=SUM(1)", mandatory = true });
        var csv = await client.GetAsync($"/api/v1/proposals/{proposalId}/exports/compliance-matrix.csv");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.True(csv.Headers.CacheControl?.NoStore);
        Assert.EndsWith("-compliance-matrix.csv", csv.Content.Headers.ContentDisposition?.FileNameStar ?? csv.Content.Headers.ContentDisposition?.FileName?.Trim('"'), StringComparison.Ordinal);
        Assert.Contains("L.1,'=SUM(1)", await csv.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var outline = await client.GetAsync($"/api/v1/proposals/{proposalId}/exports/outline.md");
        Assert.Contains("## Sections", await outline.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var other = await _factory.CreateCompanyUserClientAsync("Docs Other");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/documents/{templateId}/versions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/documents/{templateId}/versions/1/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/documents/{templateId}/copy", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/proposals/{proposalId}/exports/compliance-matrix.csv")).StatusCode);
        using var anonymous = _factory.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/proposals/{proposalId}/exports/outline.md")).StatusCode);
    }
}
