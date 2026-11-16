using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovBidPro.ApiTests;

/// <summary>P1.7 guardrail: a new API controller cannot ship without an authorization policy.</summary>
public sealed class ControllerGuardTests
{
    [Fact]
    public void Every_api_controller_requires_authorization_and_allows_no_anonymous_action()
    {
        var controllers = typeof(Program).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t)).ToList();
        Assert.True(controllers.Count >= 10, "Expected the API controllers to be discovered.");
        foreach (var controller in controllers)
        {
            var authorize = controller.GetCustomAttribute<AuthorizeAttribute>();
            Assert.True(authorize is not null, $"{controller.Name} has no [Authorize].");

            // Only the "who am I" endpoint may serve a signed-in user who has no company yet.
            Assert.True(controller.Name == "MeController" || !string.IsNullOrEmpty(authorize.Policy), $"{controller.Name} has no named authorization policy.");
            Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.DoesNotContain(controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly), m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null);
        }
    }
}
