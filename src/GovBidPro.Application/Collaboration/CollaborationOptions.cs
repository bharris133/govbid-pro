using GovBidPro.Domain.Enums;

namespace GovBidPro.Application.Collaboration;

/// <summary>
/// Defaults for the open product questions on membership and alerts (docs/decisions/open-questions.md).
/// Each can be changed in configuration without a code change.
/// </summary>
public sealed class CollaborationOptions
{
    public const string SectionName = "Collaboration";

    /// <summary>Days before an unused invitation stops working.</summary>
    public int InvitationExpiryDays { get; set; } = 7;

    /// <summary>Lowest role that may invite people and revoke invitations.</summary>
    public CompanyRole MinimumRoleToInvite { get; set; } = CompanyRole.CompanyAdmin;

    /// <summary>Lowest role that may change roles and remove members.</summary>
    public CompanyRole MinimumRoleToManageMembers { get; set; } = CompanyRole.CompanyAdmin;

    /// <summary>Accepting an invitation requires a confirmed email address on the account.</summary>
    public bool RequireConfirmedEmailToAcceptInvitation { get; set; } = true;

    public int MaxPendingInvitationsPerCompany { get; set; } = 100;

    /// <summary>Send notifications by email as well as in the app (members can opt out individually).</summary>
    public bool EmailNotificationsEnabled { get; set; } = true;

    /// <summary>Absolute https origin used for links in emails, for example https://app.example.com. Required when email is on.</summary>
    public string? PublicBaseUrl { get; set; }

    public SavedSearchAlertOptions Alerts { get; set; } = new();

    public static bool IsValid(CollaborationOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return o.InvitationExpiryDays is >= 1 and <= 90
            && Enum.IsDefined(o.MinimumRoleToInvite) && Enum.IsDefined(o.MinimumRoleToManageMembers)
            && o.MaxPendingInvitationsPerCompany is >= 1 and <= 10000
            && (string.IsNullOrWhiteSpace(o.PublicBaseUrl) || (Uri.TryCreate(o.PublicBaseUrl, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query)))
            && o.Alerts.IntervalMinutes is >= 5 and <= 1440 && o.Alerts.DailyEveryHours is >= 1 and <= 168 && o.Alerts.WeeklyEveryDays is >= 1 and <= 31
            && o.Alerts.MaxItemsPerAlert is >= 1 and <= 100 && o.Alerts.BatchSize is >= 1 and <= 1000;
    }

    /// <summary>
    /// Whether <paramref name="publicBaseUrl"/> may be used in links that are emailed: https everywhere, or
    /// (Development/Testing only) plain http on this machine, so a developer can test real email delivery locally.
    /// </summary>
    public static bool IsUsableForEmail(string? publicBaseUrl, bool isLocalEnvironment) =>
        Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (isLocalEnvironment && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

    /// <summary>Absolute link for emails, or null when no public address is configured.</summary>
    public string? AbsoluteLink(string? path) =>
        string.IsNullOrWhiteSpace(PublicBaseUrl) || string.IsNullOrWhiteSpace(path) ? null : PublicBaseUrl.TrimEnd('/') + path;
}

public sealed class SavedSearchAlertOptions
{
    /// <summary>Runs the saved-search alert job. Alerts always appear in the app; email follows the email settings.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the job looks for searches that are due.</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>"Daily" alerts go out at most once per this many hours.</summary>
    public int DailyEveryHours { get; set; } = 24;

    /// <summary>"Weekly" alerts go out at most once per this many days.</summary>
    public int WeeklyEveryDays { get; set; } = 7;

    public int MaxItemsPerAlert { get; set; } = 20;

    public int BatchSize { get; set; } = 200;
}
