namespace GovBidPro.Application.Abstractions;

/// <summary>One outgoing email. Bodies are plain text unless <see cref="HtmlBody"/> is given.</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody = null);

/// <summary>
/// Provider-neutral outgoing email (cloud portability, ADR-0004). Adapters live in Infrastructure:
/// SMTP works with any provider; a cloud-specific API adapter can be added without touching callers.
/// Implementations throw on delivery failure; callers decide whether that is fatal.
/// </summary>
public interface IEmailSender
{
    /// <summary>False when no provider is configured, so callers can skip work and tell users.</summary>
    bool IsEnabled { get; }

    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
