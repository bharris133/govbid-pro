using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovBidPro.Testing;

namespace GovBidPro.ApiTests;

/// <summary>P1.4 over HTTP: start a response, compliance matrix, status gates, problem details and tenant scoping.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class ProposalApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private GovBidProFactory _factory = null!;

    public async Task InitializeAsync() => _factory = new GovBidProFactory(await postgres.CreateEmptyDatabaseAsync());

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static async Task<string> FirstOpportunityIdAsync(HttpClient client)
    {
        using var list = JsonDocument.Parse(await client.GetStringAsync("/api/v1/opportunities?pageSize=1"));
        return list.RootElement.GetProperty("results").GetProperty("items")[0].GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Company_user_can_start_a_response_build_the_matrix_and_move_it_forward()
    {
        using var client = await _factory.CreateCompanyUserClientAsync("Proposal Co");
        var opportunityId = await FirstOpportunityIdAsync(client);

        var created = await client.PostAsJsonAsync("/api/v1/proposals", new { opportunityId });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var proposal = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = proposal.RootElement.GetProperty("id").GetString()!;
        Assert.Equal("Draft", proposal.RootElement.GetProperty("status").GetString());
        Assert.True(proposal.RootElement.GetProperty("sections").GetArrayLength() > 0);

        var duplicate = await client.PostAsJsonAsync("/api/v1/proposals", new { opportunityId });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("application/problem+json", duplicate.Content.Headers.ContentType?.MediaType);

        var requirement = await client.PostAsJsonAsync($"/api/v1/proposals/{id}/requirements", new { reference = "L.4.2", text = "Describe the staffing plan.", mandatory = true });
        Assert.Equal(HttpStatusCode.Created, requirement.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/proposals/{id}/requirements", new { reference = "", text = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/proposals/{id}/requirements", new { reference = "l.4.2", text = "Duplicate" })).StatusCode);

        using var matrix = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/proposals/{id}/requirements"));
        Assert.Equal("L.4.2", matrix.RootElement[0].GetProperty("reference").GetString());

        var moved = await client.PostAsJsonAsync($"/api/v1/proposals/{id}/status", new { to = "InProgress", note = "Kickoff" });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var skipped = await client.PostAsJsonAsync($"/api/v1/proposals/{id}/status", new { to = "SubmittedExternally" });
        Assert.Equal(HttpStatusCode.Conflict, skipped.StatusCode);

        using var list = JsonDocument.Parse(await client.GetStringAsync("/api/v1/proposals"));
        var row = list.RootElement[0];
        Assert.Equal("In progress", row.GetProperty("statusLabel").GetString());
        Assert.Equal(1, row.GetProperty("compliance").GetProperty("mandatoryOpen").GetInt32());

        using var members = JsonDocument.Parse(await client.GetStringAsync("/api/v1/company/members"));
        Assert.Equal(1, members.RootElement.GetArrayLength());

        // P1.7: the company admin can download the company's own audit trail; it is never cached.
        var auditExport = await client.GetAsync("/api/v1/company/audit/export.csv");
        Assert.Equal(HttpStatusCode.OK, auditExport.StatusCode);
        Assert.Equal("text/csv", auditExport.Content.Headers.ContentType?.MediaType);
        Assert.True(auditExport.Headers.CacheControl?.NoStore);
        var auditCsv = await auditExport.Content.ReadAsStringAsync();
        Assert.Contains("proposal.created", auditCsv, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/company/audit/export.csv?from=2020-01-01T00:00:00Z&to=2026-01-01T00:00:00Z")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Proposals")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Proposals/Detail?id={id}")).StatusCode);
    }

    [Fact]
    public async Task Proposals_are_private_to_the_company()
    {
        using var a = await _factory.CreateCompanyUserClientAsync("Proposal A");
        using var b = await _factory.CreateCompanyUserClientAsync("Proposal B");
        var opportunityId = await FirstOpportunityIdAsync(a);
        using var created = JsonDocument.Parse(await (await a.PostAsJsonAsync("/api/v1/proposals", new { opportunityId })).Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString();

        Assert.Equal("[]", await b.GetStringAsync("/api/v1/proposals"));
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/v1/proposals/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync($"/api/v1/proposals/{id}/status", new { to = "Cancelled" })).StatusCode);

        using var anonymous = _factory.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/proposals")).StatusCode);
    }
}
