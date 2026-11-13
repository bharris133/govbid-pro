using GovBidPro.Domain.Common;
using GovBidPro.Domain.Enums;

namespace GovBidPro.Domain.Entities;

/// <summary>A customer company. The company Id is the tenant Id for all tenant-owned data.</summary>
public sealed class Company
{
    private Company()
    {
        DisplayName = string.Empty;
    }

    public Company(Guid id, string displayName, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("Company id is required.");
        }

        Id = id;
        DisplayName = NormalizeName(displayName);
        CreatedAt = createdAt;
        Status = TenantStatus.Active;
    }

    public Guid Id { get; private set; }

    public string DisplayName { get; private set; }

    public TenantStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Tenant-level AI consent. AI is OFF by default (D-03, AI-AZ-05). Enabling requires a company
    /// administrator and is still subject to the platform-level switch and approval gates AI-01/AI-02.
    /// </summary>
    public bool AiEnabled { get; private set; }

    public string? AiConsentedByUserId { get; private set; }

    public DateTimeOffset? AiConsentedAt { get; private set; }

    public bool IsActive => Status == TenantStatus.Active;

    public void Rename(string displayName) => DisplayName = NormalizeName(displayName);

    public void RecordAiConsent(string userId, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        AiEnabled = true;
        AiConsentedByUserId = userId;
        AiConsentedAt = at;
    }

    public void WithdrawAiConsent()
    {
        AiEnabled = false;
        AiConsentedByUserId = null;
        AiConsentedAt = null;
    }

    public void Suspend() => Status = TenantStatus.Suspended;

    private static string NormalizeName(string displayName)
    {
        var trimmed = displayName?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 200)
        {
            throw new DomainException("Company display name must be 1-200 characters.");
        }

        return trimmed;
    }
}
