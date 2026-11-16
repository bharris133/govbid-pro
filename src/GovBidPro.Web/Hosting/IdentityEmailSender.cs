using System.Net;
using System.Text.RegularExpressions;
using GovBidPro.Application.Abstractions;

namespace GovBidPro.Web.Hosting;

/// <summary>
/// Routes the sign-in system's account emails (confirm address, reset password) through the
/// provider-neutral email port, so they use whichever provider the environment configures.
/// </summary>
internal sealed partial class IdentityEmailSender(IEmailSender email) : Microsoft.AspNetCore.Identity.UI.Services.IEmailSender
{
    public Task SendEmailAsync(string email1, string subject, string htmlMessage)
    {
        var text = WebUtility.HtmlDecode(Tags().Replace(Links().Replace(htmlMessage, "$2 ($1)"), string.Empty));
        return email.IsEnabled ? email.SendAsync(new EmailMessage(email1, subject, text, htmlMessage), CancellationToken.None) : Task.CompletedTask;
    }

    [GeneratedRegex("<a\\s[^>]*href=['\"]([^'\"]+)['\"][^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Links();

    [GeneratedRegex("<[^>]+>", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Tags();
}
