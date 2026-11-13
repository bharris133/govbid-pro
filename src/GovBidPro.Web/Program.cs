using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using GovBidPro.Application.Abstractions;
using GovBidPro.Infrastructure;
using GovBidPro.Infrastructure.Identity;
using GovBidPro.Infrastructure.Persistence;
using GovBidPro.Web.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;
var configuration = builder.Configuration;

services.AddGovBidProCore(configuration, builder.Environment);

// Plain-HTTP cookies are permitted only for local Development (Docker Compose, CI smoke tests).
// Hosted environments terminate TLS at Azure Container Apps ingress and always use Secure cookies.
var allowInsecureLocalHttp = configuration.GetValue<bool>("Security:AllowInsecureLocalHttp");
if (allowInsecureLocalHttp && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("Security:AllowInsecureLocalHttp is only permitted in Development.");
}

var cookieSecurePolicy = allowInsecureLocalHttp ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
var cookiePrefix = allowInsecureLocalHttp ? "govbid-local" : "__Host-govbid";
services.AddHttpContextAccessor();
services.AddScoped<ICorrelationContext, HttpCorrelationContext>();

// Identity: secure HttpOnly cookies, lockout-based login throttling, MFA (TOTP) via Identity UI pages.
services.AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = configuration.GetValue("Identity:RequireConfirmedAccount", true);
        options.User.RequireUniqueEmail = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version2;
        options.Stores.MaxLengthForKeys = 128;
        options.Password.RequiredLength = 12;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<GovBidProDbContext>();

services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = cookiePrefix;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnRedirectToLogin = context => ApiAwareRedirect(context.HttpContext, context.RedirectUri, StatusCodes.Status401Unauthorized);
    options.Events.OnRedirectToAccessDenied = context => ApiAwareRedirect(context.HttpContext, context.RedirectUri, StatusCodes.Status403Forbidden);
});

services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = cookiePrefix + "-csrf";
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

services.AddAuthorization(Policies.Configure);
services.AddScoped<IAuthorizationHandler, TenantMemberHandler>();

services.AddControllers(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Opportunities");
    options.Conventions.AuthorizeFolder("/Documents");
    options.Conventions.AuthorizeFolder("/Onboarding");
});

services.AddProblemDetails();
services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
services.AddHealthChecks().AddDbContextCheck<GovBidProDbContext>("database", tags: ["ready"]);
services.AddHostedService<RetentionPurgeHostedService>();

services.Configure<ForwardedHeadersOptions>(options =>
{
    // Azure Container Apps terminates TLS at its ingress and forwards the original scheme.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var isLogin = context.Request.Path.StartsWithSegments("/Identity/Account/Login", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsPost(context.Request.Method);
        var partition = (isLogin ? "login:" : "req:") + (context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = isLogin ? configuration.GetValue("RateLimiting:LoginPerMinute", 10) : configuration.GetValue("RateLimiting:RequestsPerMinute", 300),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();
app.MapRazorPages();

await app.InitializeGovBidProAsync();
await app.RunAsync();

static Task ApiAwareRedirect(HttpContext context, string redirectUri, int apiStatusCode)
{
    if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = apiStatusCode;
        return Task.CompletedTask;
    }

    context.Response.Redirect(redirectUri);
    return Task.CompletedTask;
}

/// <summary>Entry point marker for WebApplicationFactory in API tests.</summary>
public partial class Program;
