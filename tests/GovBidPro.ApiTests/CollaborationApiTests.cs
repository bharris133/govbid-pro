using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GovBidPro.Testing;

namespace GovBidPro.ApiTests;

/// <summary>P1.6 over HTTP: invite and join, member administration, tasks, notifications and comments.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed partial class CollaborationApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private GovBidProFactory _factory = null!;

    public async Task InitializeAsync() => _factory = new GovBidProFactory(await postgres.CreateEmptyDatabaseAsync());

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task Invited_member_joins_collaborates_and_loses_access_when_removed()
    {
        using var admin = await _factory.CreateCompanyUserClientAsync("Team Co");
        var inviteeEmail = await _factory.CreateUserAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/company/invitations", new { email = "nope", role = "Reviewer" })).StatusCode);
        var invited = await admin.PostAsJsonAsync("/api/v1/company/invitations", new { email = inviteeEmail, role = "ProposalWriter" });
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        Assert.True(invited.Headers.CacheControl?.NoStore);
        var created = await JsonAsync(invited);
        var acceptPath = created.GetProperty("acceptPath").GetString()!;
        Assert.True(created.GetProperty("emailSent").GetBoolean());
        Assert.StartsWith("/Invitations/Accept?token=", acceptPath, StringComparison.Ordinal);
        using var listed = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/company/invitations"));
        Assert.Equal("Pending", listed.RootElement[0].GetProperty("state").GetString());
        Assert.DoesNotContain("token", listed.RootElement[0].ToString(), StringComparison.OrdinalIgnoreCase);

        // The link needs a signed-in account.
        using var anonymous = _factory.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync(acceptPath)).StatusCode);

        using var member = _factory.CreateBrowserClient();
        await GovBidProFactory.SignInAsync(member, inviteeEmail);
        var page = await member.GetAsync(acceptPath);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.True(page.Headers.CacheControl?.NoStore);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Team Co", html, StringComparison.Ordinal);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Token"] = acceptPath.Split("token=")[1],
            ["__RequestVerificationToken"] = FormToken().Match(html).Groups[1].Value,
        });
        var accepted = await member.PostAsync("/Invitations/Accept", form);
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.Equal("/Dashboard", accepted.Headers.Location?.OriginalString);
        await GovBidProFactory.UseCsrfHeaderAsync(member);

        using var me = JsonDocument.Parse(await member.GetStringAsync("/api/v1/me"));
        Assert.Equal("ProposalWriter", me.RootElement.GetProperty("role").GetString());
        Assert.Equal("Team Co", me.RootElement.GetProperty("companyName").GetString());
        var memberId = me.RootElement.GetProperty("userId").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/company/invitations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/v1/company/invitations", new { email = "x@example.test", role = "Reviewer" })).StatusCode);
        Assert.Contains("no longer valid", await member.GetStringAsync(acceptPath), StringComparison.Ordinal);

        // The inviter was told; the task assignment reaches the new member.
        using var adminNotes = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/notifications"));
        Assert.Equal("member.joined", adminNotes.RootElement.GetProperty("items")[0].GetProperty("kind").GetString());
        var task = await admin.PostAsJsonAsync("/api/v1/tasks", new { title = "Collect past performance", assigneeUserId = memberId, dueOn = "2027-01-15" });
        Assert.Equal(HttpStatusCode.Created, task.StatusCode);
        var taskId = (await JsonAsync(task)).GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/tasks", new { title = "", assigneeUserId = memberId })).StatusCode);

        using var count = JsonDocument.Parse(await member.GetStringAsync("/api/v1/notifications/unread-count"));
        Assert.Equal(1, count.RootElement.GetProperty("unread").GetInt32());
        using var mine = JsonDocument.Parse(await member.GetStringAsync("/api/v1/tasks?mine=true"));
        Assert.Equal(taskId, mine.RootElement[0].GetProperty("id").GetString());
        Assert.Equal("2027-01-15", mine.RootElement[0].GetProperty("dueOn").GetString());
        Assert.Equal("Done", (await JsonAsync(await member.PostAsJsonAsync($"/api/v1/tasks/{taskId}/status", new { status = "Done" }))).GetProperty("status").GetString());
        Assert.Equal(1, (await JsonAsync(await member.PostAsync("/api/v1/notifications/read-all", null))).GetProperty("marked").GetInt32());
        var preferences = await JsonAsync(await member.PutAsJsonAsync("/api/v1/notifications/preferences", new { emailNotifications = false }));
        Assert.False(preferences.GetProperty("emailNotifications").GetBoolean());
        Assert.True(preferences.GetProperty("emailAvailable").GetBoolean());

        // Comments on a proposal.
        using var opportunities = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/opportunities?pageSize=1"));
        var opportunityId = opportunities.RootElement.GetProperty("results").GetProperty("items")[0].GetProperty("id").GetString();
        var proposalId = (await JsonAsync(await admin.PostAsJsonAsync("/api/v1/proposals", new { opportunityId }))).GetProperty("id").GetString();
        var commented = await member.PostAsJsonAsync($"/api/v1/proposals/{proposalId}/comments", new { body = "<b>Looks</b> good so far." });
        Assert.Equal(HttpStatusCode.Created, commented.StatusCode);
        var comment = (await JsonAsync(commented))[0];
        Assert.Equal("<b>Looks</b> good so far.", comment.GetProperty("body").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/proposals/{proposalId}/comments/{comment.GetProperty("id").GetString()}")).StatusCode);
        using var comments = JsonDocument.Parse(await member.GetStringAsync($"/api/v1/proposals/{proposalId}/comments"));
        Assert.True(comments.RootElement[0].GetProperty("deleted").GetBoolean());

        foreach (var path in new[] { "/Tasks", "/Notifications", "/Company" })
        {
            Assert.Equal(HttpStatusCode.OK, (await member.GetAsync(path)).StatusCode);
        }

        // Another company sees none of it.
        using var outsider = await _factory.CreateCompanyUserClientAsync("Outside Co");
        Assert.Equal("[]", await outsider.GetStringAsync("/api/v1/tasks?includeClosed=true"));
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/v1/proposals/{proposalId}/comments")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsJsonAsync($"/api/v1/tasks/{taskId}/status", new { status = "Cancelled" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.DeleteAsync($"/api/v1/company/members/{memberId}")).StatusCode);

        // Role change and removal.
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PutAsJsonAsync($"/api/v1/company/members/{memberId}/role", new { role = "CompanyAdmin" })).StatusCode);
        Assert.Equal("Reviewer", (await JsonAsync(await admin.PutAsJsonAsync($"/api/v1/company/members/{memberId}/role", new { role = "Reviewer" }))).GetProperty("role").GetString());
        using var adminMe = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/me"));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/v1/company/members/{adminMe.RootElement.GetProperty("userId").GetString()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/company/members/{memberId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/proposals")).StatusCode);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex FormToken();
}
