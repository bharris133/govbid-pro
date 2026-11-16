using System.Net;
using System.Net.Mail;
using GovBidPro.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GovBidPro.Infrastructure.Email;

public static class EmailProviders
{
    public const string None = "None";
    public const string Log = "Log";
    public const string Smtp = "Smtp";

    public static bool IsKnown(string? provider) => provider is None or Log or Smtp;
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>"None" (default: nothing is sent), "Log" (Development/Testing only: written to the log) or "Smtp".</summary>
    public string Provider { get; set; } = EmailProviders.None;

    public string? FromAddress { get; set; }

    public string FromName { get; set; } = "GovBid Pro";

    public SmtpOptions Smtp { get; set; } = new();

    public static bool IsValid(EmailOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return o.Provider != EmailProviders.Smtp
            || (!string.IsNullOrWhiteSpace(o.Smtp.Host) && o.Smtp.Port is > 0 and <= 65535 && MailAddress.TryCreate(o.FromAddress, out _) && o.Smtp.TimeoutSeconds is > 0 and <= 300);
    }
}

public sealed class SmtpOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    /// <summary>Require TLS (STARTTLS on 587). Only turn off for a local test relay.</summary>
    public bool UseTls { get; set; } = true;

    public string? Username { get; set; }

    /// <summary>Supply through the environment's secret store (for example Email__Smtp__Password), never in a file.</summary>
    public string? Password { get; set; }

    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>Default: email is off. Nothing is sent and callers see <see cref="IsEnabled"/> false.</summary>
internal sealed class DisabledEmailSender : IEmailSender
{
    public bool IsEnabled => false;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Development and tests only: writes the message to the application log instead of sending it.</summary>
internal sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public bool IsEnabled => true;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        // The recipient is masked and everything is flattened to one line, so text a user typed (an invited
        // address, a proposal title) can neither forge log entries nor put a full address in the log.
        logger.LogInformation("DEV EMAIL (not sent) to {To}: {Subject} | {Body}", MaskAddress(message.To), Redact(message.Subject), Redact(message.TextBody));
        return Task.CompletedTask;
    }

    internal static string MaskAddress(string address)
    {
        var at = address.LastIndexOf('@');
        return at <= 0 ? "***" : SingleLine(string.Concat(address.AsSpan(0, 1), "***", address.AsSpan(at)));
    }

    /// <summary>One line, with any email address inside the text masked (links are kept so a developer can follow them).</summary>
    internal static string Redact(string value) =>
        string.Join(' ', SingleLine(value).Split(' ').Select(word => word.Contains('@', StringComparison.Ordinal) && !word.Contains("://", StringComparison.Ordinal) ? "[address]" : word));

    internal static string SingleLine(string value) =>
        new string(value.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " | ", StringComparison.Ordinal).Where(c => !char.IsControl(c)).ToArray());
}

/// <summary>
/// Standard SMTP submission. Works with any provider that offers SMTP (cloud email services, a company
/// relay), which keeps email portable. Recipients and content are not logged.
/// </summary>
internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public bool IsEnabled => true;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;
        using var mail = new MailMessage
        {
            From = new MailAddress(settings.FromAddress!, settings.FromName),
            Subject = message.Subject.ReplaceLineEndings(" "),
            Body = message.HtmlBody ?? message.TextBody,
            IsBodyHtml = message.HtmlBody is not null,
        };
        mail.To.Add(new MailAddress(message.To));
        using var client = new SmtpClient(settings.Smtp.Host, settings.Smtp.Port)
        {
            EnableSsl = settings.Smtp.UseTls,
            Timeout = settings.Smtp.TimeoutSeconds * 1000,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };
        if (!string.IsNullOrWhiteSpace(settings.Smtp.Username))
        {
            client.Credentials = new NetworkCredential(settings.Smtp.Username, settings.Smtp.Password);
        }

        await client.SendMailAsync(mail, cancellationToken);
    }
}
