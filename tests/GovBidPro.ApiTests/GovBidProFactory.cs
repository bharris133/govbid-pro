using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using GovBidPro.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GovBidPro.ApiTests;

/// <summary>Hosts the real application (Testing environment) against a throwaway PostgreSQL database.</summary>
public sealed partial class GovBidProFactory(string connectionString) : WebApplicationFactory<Program>
{
    public const string Password = "Api-Test-Passw0rd!";
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "govbid-api-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:GovBidPro"] = connectionString,
            ["Database:ApplyMigrationsOnStartup"] = "true",
            ["Identity:RequireConfirmedAccount"] = "false",
            ["Storage:Provider"] = "Local",
            ["Storage:LocalRootPath"] = _storageRoot,
            ["Documents:MalwareScanMode"] = "DevelopmentBypass",
            ["OpportunitySources:Fixture:ImportOnStartup"] = "true",
            ["RateLimiting:RequestsPerMinute"] = "10000",
            ["RateLimiting:LoginPerMinute"] = "10000",
        }));
    }

    public HttpClient CreateBrowserClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });

    public async Task<string> CreateUserAsync(string? role = null)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"user-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await users.CreateAsync(user, Password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        if (role is not null)
        {
            await users.AddToRoleAsync(user, role);
        }

        return email;
    }

    /// <summary>Signs in through the real Identity login page (antiforgery + cookie).</summary>
    public static async Task SignInAsync(HttpClient client, string email)
    {
        var page = await client.GetStringAsync("/Identity/Account/Login");
        var token = FormToken().Match(page).Groups[1].Value;
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = Password,
            ["Input.RememberMe"] = "false",
            ["__RequestVerificationToken"] = token,
        });
        var response = await client.PostAsync("/Identity/Account/Login", form);
        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException($"Login failed with {response.StatusCode}.");
        }
    }

    /// <summary>Creates a company for the signed-in user through the onboarding page.</summary>
    public static async Task CreateCompanyAsync(HttpClient client, string name)
    {
        var page = await client.GetStringAsync("/Onboarding/CreateCompany");
        var token = FormToken().Match(page).Groups[1].Value;
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["CompanyName"] = name, ["__RequestVerificationToken"] = token });
        var response = await client.PostAsync("/Onboarding/CreateCompany", form);
        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException($"Company creation failed with {response.StatusCode}.");
        }
    }

    /// <summary>Reads the antiforgery request token from the layout meta tag and sets it as the API header.</summary>
    public static async Task UseCsrfHeaderAsync(HttpClient client)
    {
        var page = await client.GetStringAsync("/Opportunities");
        var token = MetaToken().Match(page).Groups[1].Value;
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", WebUtility.HtmlDecode(token));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<HttpClient> CreateCompanyUserClientAsync(string companyName)
    {
        var client = CreateBrowserClient();
        await SignInAsync(client, await CreateUserAsync());
        await CreateCompanyAsync(client, companyName);
        await UseCsrfHeaderAsync(client);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex FormToken();

    [GeneratedRegex("<meta name=\"csrf-token\" content=\"([^\"]+)\"")]
    private static partial Regex MetaToken();
}
