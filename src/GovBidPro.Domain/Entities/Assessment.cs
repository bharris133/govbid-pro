using GovBidPro.Domain.Common;

namespace GovBidPro.Domain.Entities;

/// <summary>Set-aside programs a company may state it qualifies for. Self-declared: GovBid Pro never certifies eligibility.</summary>
public enum SetAsideCategory
{
    SmallBusiness = 0,
    EightA = 1,
    HubZone = 2,
    Sdvosb = 3,
    Vosb = 4,
    Wosb = 5,
    Edwosb = 6,
    IndianEconomicEnterprise = 7,
}

public enum GoNoGoDecision
{
    /// <summary>Scored but no decision recorded yet.</summary>
    Undecided = 0,
    Go = 1,
    NoGo = 2,

    /// <summary>Keep watching (for example a presolicitation); decide later.</summary>
    Watch = 3,
}

/// <summary>What the company tells GovBid Pro about itself so opportunities can be scored. Stored as JSON.</summary>
public sealed class CompanyFitProfile
{
    public List<string> NaicsCodes { get; set; } = [];

    public List<SetAsideCategory> SetAsideQualifications { get; set; } = [];

    public List<string> PreferredStates { get; set; } = [];

    public List<string> PreferredAgencies { get; set; } = [];

    public List<string> CapabilityKeywords { get; set; } = [];

    /// <summary>Fewest days the team wants between today and the response deadline.</summary>
    public int MinimumResponseDays { get; set; } = 10;

    public bool IsEmpty => NaicsCodes.Count == 0 && SetAsideQualifications.Count == 0 && PreferredStates.Count == 0 && PreferredAgencies.Count == 0 && CapabilityKeywords.Count == 0;

    /// <summary>Trims, de-duplicates and bounds every list so stored profiles stay small and predictable.</summary>
    public CompanyFitProfile Normalized()
    {
        static List<string> Clean(IEnumerable<string>? values, int maxLength, bool upper = false) =>
            (values ?? [])
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Select(v => v.Length > maxLength ? v[..maxLength] : v)
                .Select(v => upper ? v.ToUpperInvariant() : v)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(50)
                .ToList();

        if (MinimumResponseDays is < 0 or > 365)
        {
            throw new DomainException("Minimum response days must be between 0 and 365.");
        }

        var naics = Clean(NaicsCodes, 6);
        if (naics.Any(code => code.Length < 2 || !code.All(char.IsAsciiDigit)))
        {
            throw new DomainException("NAICS codes must be 2 to 6 digits.");
        }

        return new CompanyFitProfile
        {
            NaicsCodes = naics,
            SetAsideQualifications = (SetAsideQualifications ?? []).Where(Enum.IsDefined).Distinct().ToList(),
            PreferredStates = Clean(PreferredStates, 2, upper: true),
            PreferredAgencies = Clean(PreferredAgencies, 100),
            CapabilityKeywords = Clean(CapabilityKeywords, 60),
            MinimumResponseDays = MinimumResponseDays,
        };
    }
}

/// <summary>One per company. Feeds the explainable fit score (P1.3).</summary>
public sealed class CompanyProfile : ITenantOwned
{
    private CompanyProfile()
    {
        Fit = new CompanyFitProfile();
        UpdatedByUserId = string.Empty;
    }

    public CompanyProfile(Guid id, Guid tenantId, CompanyFitProfile fit, string updatedByUserId, DateTimeOffset updatedAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new DomainException("Tenant id is required.");
        }

        ArgumentNullException.ThrowIfNull(fit);
        ArgumentException.ThrowIfNullOrWhiteSpace(updatedByUserId);
        Id = id;
        TenantId = tenantId;
        Fit = fit.Normalized();
        UpdatedByUserId = updatedByUserId;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public CompanyFitProfile Fit { get; private set; }

    public string UpdatedByUserId { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(CompanyFitProfile fit, string userId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(fit);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        Fit = fit.Normalized();
        UpdatedByUserId = userId;
        UpdatedAt = at;
    }
}

/// <summary>
/// An immutable, company-specific assessment of one opportunity: the fit score as calculated at that
/// moment, the criteria behind it, and the Go/No-go decision with its author and rationale. A new
/// decision appends a new record, so the sequence of records is the audit trail (handoff §5, P1.3).
/// </summary>
public sealed class OpportunityAssessment : ITenantOwned
{
    public const int MaxRationaleLength = 2000;

    private OpportunityAssessment()
    {
        RuleVersion = string.Empty;
        Band = string.Empty;
        CriteriaJson = "[]";
        CreatedByUserId = string.Empty;
    }

    public OpportunityAssessment(
        Guid id,
        Guid tenantId,
        Guid opportunityId,
        int sequence,
        int opportunityRevisionNumber,
        int? score,
        string band,
        string ruleVersion,
        string criteriaJson,
        GoNoGoDecision decision,
        string? rationale,
        string createdByUserId,
        DateTimeOffset createdAt)
    {
        if (tenantId == Guid.Empty || opportunityId == Guid.Empty)
        {
            throw new DomainException("Tenant and opportunity are required.");
        }

        if (score is < 0 or > 100)
        {
            throw new DomainException("Score must be between 0 and 100.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(createdByUserId);
        var trimmed = string.IsNullOrWhiteSpace(rationale) ? null : rationale.Trim();
        if (trimmed is { Length: > MaxRationaleLength })
        {
            throw new DomainException($"Rationale must be at most {MaxRationaleLength} characters.");
        }

        if (decision is GoNoGoDecision.Go or GoNoGoDecision.NoGo && trimmed is null)
        {
            throw new DomainException("A Go or No-go decision needs a rationale.");
        }

        Id = id;
        TenantId = tenantId;
        OpportunityId = opportunityId;
        Sequence = sequence;
        OpportunityRevisionNumber = opportunityRevisionNumber;
        Score = score;
        Band = band;
        RuleVersion = ruleVersion;
        CriteriaJson = criteriaJson;
        Decision = decision;
        Rationale = trimmed;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid OpportunityId { get; private set; }

    /// <summary>1-based order of this record within the company's history for the opportunity.</summary>
    public int Sequence { get; private set; }

    /// <summary>The notice revision that was scored, so later amendments are visible as "scored against an older revision".</summary>
    public int OpportunityRevisionNumber { get; private set; }

    public int? Score { get; private set; }

    public string Band { get; private set; }

    public string RuleVersion { get; private set; }

    /// <summary>Snapshot of the criteria results that produced <see cref="Score"/>.</summary>
    public string CriteriaJson { get; private set; }

    public GoNoGoDecision Decision { get; private set; }

    public string? Rationale { get; private set; }

    public string CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
