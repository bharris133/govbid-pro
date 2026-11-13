using GovBidPro.Domain.Common;
using GovBidPro.Domain.Enums;

namespace GovBidPro.Domain.Entities;

/// <summary>A company-owned saved opportunity search (handoff §5). Matching/alerts arrive in P1.</summary>
public sealed class SavedSearch : ITenantOwned
{
    private SavedSearch()
    {
        Name = string.Empty;
        CreatedByUserId = string.Empty;
        Criteria = new OpportunitySearchCriteria();
    }

    public SavedSearch(Guid id, Guid tenantId, string name, OpportunitySearchCriteria criteria, NotificationFrequency frequency, string createdByUserId, DateTimeOffset createdAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new DomainException("Tenant id is required.");
        }

        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdByUserId);
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 120)
        {
            throw new DomainException("Saved search name must be 1-120 characters.");
        }

        Id = id;
        TenantId = tenantId;
        Name = trimmed;
        Criteria = criteria;
        NotificationFrequency = frequency;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; }

    public OpportunitySearchCriteria Criteria { get; private set; }

    public NotificationFrequency NotificationFrequency { get; private set; }

    public string CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastEvaluatedAt { get; private set; }
}

/// <summary>Structured search filters. Stored as JSON; all list filters are OR within a field and AND across fields.</summary>
public sealed class OpportunitySearchCriteria
{
    public string? Keywords { get; set; }

    public List<string> NaicsCodes { get; set; } = [];

    public List<string> Agencies { get; set; } = [];

    public List<SourceNoticeType> NoticeTypes { get; set; } = [];

    public List<ResponseType> ResponseTypes { get; set; } = [];

    public List<string> SetAsides { get; set; } = [];

    public List<string> PlaceOfPerformanceStates { get; set; } = [];

    public DateTimeOffset? PostedFrom { get; set; }

    public DateTimeOffset? PostedTo { get; set; }

    public DateTimeOffset? DueAfter { get; set; }
}
