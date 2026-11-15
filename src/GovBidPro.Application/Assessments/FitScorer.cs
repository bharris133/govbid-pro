using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;

namespace GovBidPro.Application.Assessments;

public enum FitOutcome
{
    Met = 0,
    Partial = 1,
    NotMet = 2,

    /// <summary>Could not be judged (missing company or notice data). Excluded from the score.</summary>
    NotEvaluated = 3,
}

public sealed record FitCriterionResult(string Key, string Label, int Weight, int Points, FitOutcome Outcome, string Explanation);

/// <param name="Score">0-100, or null when no criterion could be evaluated.</param>
/// <param name="Band">"Strong", "Possible", "Weak" or "NotScored".</param>
public sealed record FitResult(int? Score, string Band, string RuleVersion, IReadOnlyList<FitCriterionResult> Criteria, IReadOnlyList<string> Cautions);

public sealed record OpportunityFacts(
    string Title,
    string? Description,
    string? Agency,
    string? NaicsCode,
    string? SetAside,
    string? PlaceOfPerformanceState,
    SourceNoticeType NoticeType,
    DateTimeOffset? ResponseDueAt);

/// <summary>
/// Explainable, rule-based fit score (handoff P1.3). Every point comes from a named criterion with a
/// plain-language reason; nothing is learned or predicted. The score is a screening aid only: it is
/// not a win probability and it never determines set-aside eligibility.
/// </summary>
public static class FitScorer
{
    public const string RuleVersion = "fit-v1";
    public const int StrongThreshold = 70;
    public const int PossibleThreshold = 40;

    public const int NaicsWeight = 30;
    public const int SetAsideWeight = 20;
    public const int KeywordWeight = 20;
    public const int LocationWeight = 10;
    public const int AgencyWeight = 10;
    public const int TimeWeight = 10;

    public static FitResult Score(CompanyFitProfile profile, OpportunityFacts opportunity, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(opportunity);

        var cautions = new List<string>
        {
            "This score is a screening aid based on your company profile. It is not a win probability and does not determine eligibility.",
        };
        var criteria = new List<FitCriterionResult>
        {
            Naics(profile, opportunity),
            SetAside(profile, opportunity, cautions),
            Keywords(profile, opportunity),
            Location(profile, opportunity),
            Agency(profile, opportunity),
            Time(profile, opportunity, now, cautions),
        };

        if (opportunity.NoticeType == SourceNoticeType.AwardNotice)
        {
            cautions.Add("This is an award notice. No response is normally expected.");
        }

        var evaluated = criteria.Where(c => c.Outcome != FitOutcome.NotEvaluated).ToList();
        var max = evaluated.Sum(c => c.Weight);
        int? score = max == 0 ? null : (int)Math.Round(100.0 * evaluated.Sum(c => c.Points) / max, MidpointRounding.AwayFromZero);
        var band = score switch
        {
            null => "NotScored",
            >= StrongThreshold => "Strong",
            >= PossibleThreshold => "Possible",
            _ => "Weak",
        };
        if (criteria.Count != evaluated.Count)
        {
            cautions.Add("Some criteria could not be evaluated and were left out of the score. Complete the company profile for a fuller picture.");
        }

        return new FitResult(score, band, RuleVersion, criteria, cautions);
    }

    /// <summary>Reads the program a set-aside description refers to, most specific first. Null when not recognized.</summary>
    public static SetAsideCategory? ClassifySetAside(string? setAside)
    {
        if (string.IsNullOrWhiteSpace(setAside))
        {
            return null;
        }

        var text = setAside.ToUpperInvariant();
        bool Has(string value) => text.Contains(value, StringComparison.Ordinal);
        if (Has("SERVICE-DISABLED") || Has("SERVICE DISABLED") || Has("SDVOSB"))
        {
            return SetAsideCategory.Sdvosb;
        }

        if (Has("VETERAN") || Has("VOSB"))
        {
            return SetAsideCategory.Vosb;
        }

        if (Has("8(A)") || Has("8A"))
        {
            return SetAsideCategory.EightA;
        }

        if (Has("HUBZONE"))
        {
            return SetAsideCategory.HubZone;
        }

        if (Has("ECONOMICALLY DISADVANTAGED WOMEN") || Has("EDWOSB"))
        {
            return SetAsideCategory.Edwosb;
        }

        if (Has("WOMEN") || Has("WOSB"))
        {
            return SetAsideCategory.Wosb;
        }

        if (Has("INDIAN") || Has("ISBEE"))
        {
            return SetAsideCategory.IndianEconomicEnterprise;
        }

        return Has("SMALL BUSINESS") ? SetAsideCategory.SmallBusiness : null;
    }

    private static FitCriterionResult Naics(CompanyFitProfile profile, OpportunityFacts o)
    {
        const string key = "naics";
        const string label = "NAICS code";
        if (profile.NaicsCodes.Count == 0)
        {
            return new(key, label, NaicsWeight, 0, FitOutcome.NotEvaluated, "Your company profile lists no NAICS codes.");
        }

        if (string.IsNullOrWhiteSpace(o.NaicsCode))
        {
            return new(key, label, NaicsWeight, 0, FitOutcome.NotEvaluated, "The notice does not state a NAICS code.");
        }

        if (profile.NaicsCodes.Contains(o.NaicsCode, StringComparer.Ordinal))
        {
            return new(key, label, NaicsWeight, NaicsWeight, FitOutcome.Met, $"NAICS {o.NaicsCode} is in your company profile.");
        }

        var related = profile.NaicsCodes.FirstOrDefault(code => code.Length >= 4 && o.NaicsCode.Length >= 4 && string.Equals(code[..4], o.NaicsCode[..4], StringComparison.Ordinal))
            ?? profile.NaicsCodes.FirstOrDefault(code => code.Length < 6 && o.NaicsCode.StartsWith(code, StringComparison.Ordinal));
        return related is not null
            ? new(key, label, NaicsWeight, NaicsWeight / 2, FitOutcome.Partial, $"NAICS {o.NaicsCode} is in the same industry group as {related} in your profile.")
            : new(key, label, NaicsWeight, 0, FitOutcome.NotMet, $"NAICS {o.NaicsCode} is not in your company profile.");
    }

    private static FitCriterionResult SetAside(CompanyFitProfile profile, OpportunityFacts o, List<string> cautions)
    {
        const string key = "set_aside";
        const string label = "Set-aside";
        if (string.IsNullOrWhiteSpace(o.SetAside))
        {
            return new(key, label, SetAsideWeight, SetAsideWeight, FitOutcome.Met, "No set-aside is stated, so the notice appears open to all offerors.");
        }

        var category = ClassifySetAside(o.SetAside);
        if (category is null)
        {
            cautions.Add($"The set-aside “{o.SetAside}” was not recognized. Check eligibility in the solicitation.");
            return new(key, label, SetAsideWeight, 0, FitOutcome.NotEvaluated, $"Set-aside “{o.SetAside}” was not recognized.");
        }

        cautions.Add("Set-aside matching uses the programs you declared in your profile. Verify eligibility yourself; GovBid Pro does not certify it.");
        var declared = profile.SetAsideQualifications;
        var qualifies = declared.Contains(category.Value)
            || (category == SetAsideCategory.Wosb && declared.Contains(SetAsideCategory.Edwosb))
            || (category == SetAsideCategory.Vosb && declared.Contains(SetAsideCategory.Sdvosb));
        return qualifies
            ? new(key, label, SetAsideWeight, SetAsideWeight, FitOutcome.Met, $"Set-aside “{o.SetAside}” matches a program declared in your profile ({category}).")
            : new(key, label, SetAsideWeight, 0, FitOutcome.NotMet, $"Set-aside “{o.SetAside}” needs {category}, which your profile does not declare.");
    }

    private static FitCriterionResult Keywords(CompanyFitProfile profile, OpportunityFacts o)
    {
        const string key = "keywords";
        const string label = "Capability keywords";
        if (profile.CapabilityKeywords.Count == 0)
        {
            return new(key, label, KeywordWeight, 0, FitOutcome.NotEvaluated, "Your company profile lists no capability keywords.");
        }

        var text = $"{o.Title} {o.Description}";
        var matched = profile.CapabilityKeywords.Where(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matched.Count == 0)
        {
            return new(key, label, KeywordWeight, 0, FitOutcome.NotMet, "None of your capability keywords appear in the title or description.");
        }

        // Two matching keywords earn full points; one earns half.
        var points = matched.Count >= 2 ? KeywordWeight : KeywordWeight / 2;
        return new(key, label, KeywordWeight, points, matched.Count >= 2 ? FitOutcome.Met : FitOutcome.Partial, $"Found in the notice: {string.Join(", ", matched.Take(5))}.");
    }

    private static FitCriterionResult Location(CompanyFitProfile profile, OpportunityFacts o)
    {
        const string key = "location";
        const string label = "Place of performance";
        if (profile.PreferredStates.Count == 0)
        {
            return new(key, label, LocationWeight, 0, FitOutcome.NotEvaluated, "Your company profile lists no preferred states.");
        }

        if (string.IsNullOrWhiteSpace(o.PlaceOfPerformanceState))
        {
            return new(key, label, LocationWeight, 0, FitOutcome.NotEvaluated, "The notice does not state a place of performance.");
        }

        return profile.PreferredStates.Contains(o.PlaceOfPerformanceState, StringComparer.OrdinalIgnoreCase)
            ? new(key, label, LocationWeight, LocationWeight, FitOutcome.Met, $"{o.PlaceOfPerformanceState} is one of your preferred states.")
            : new(key, label, LocationWeight, 0, FitOutcome.NotMet, $"{o.PlaceOfPerformanceState} is not one of your preferred states.");
    }

    private static FitCriterionResult Agency(CompanyFitProfile profile, OpportunityFacts o)
    {
        const string key = "agency";
        const string label = "Agency";
        if (profile.PreferredAgencies.Count == 0)
        {
            return new(key, label, AgencyWeight, 0, FitOutcome.NotEvaluated, "Your company profile lists no preferred agencies.");
        }

        if (string.IsNullOrWhiteSpace(o.Agency))
        {
            return new(key, label, AgencyWeight, 0, FitOutcome.NotEvaluated, "The notice does not state an agency.");
        }

        var match = profile.PreferredAgencies.FirstOrDefault(a => o.Agency.Contains(a, StringComparison.OrdinalIgnoreCase));
        return match is not null
            ? new(key, label, AgencyWeight, AgencyWeight, FitOutcome.Met, $"“{o.Agency}” matches your preferred agency “{match}”.")
            : new(key, label, AgencyWeight, 0, FitOutcome.NotMet, $"“{o.Agency}” is not one of your preferred agencies.");
    }

    private static FitCriterionResult Time(CompanyFitProfile profile, OpportunityFacts o, DateTimeOffset now, List<string> cautions)
    {
        const string key = "time";
        const string label = "Time to respond";
        if (o.ResponseDueAt is not { } due)
        {
            return new(key, label, TimeWeight, 0, FitOutcome.NotEvaluated, "The notice does not state a response deadline that could be read as a date and time.");
        }

        var days = (int)Math.Floor((due - now).TotalDays);
        if (due < now)
        {
            cautions.Add("The response deadline has passed.");
            return new(key, label, TimeWeight, 0, FitOutcome.NotMet, "The response deadline has passed.");
        }

        if (days >= profile.MinimumResponseDays)
        {
            return new(key, label, TimeWeight, TimeWeight, FitOutcome.Met, $"{days} days remain; your profile asks for at least {profile.MinimumResponseDays}.");
        }

        var points = profile.MinimumResponseDays == 0 ? TimeWeight : TimeWeight * days / profile.MinimumResponseDays;
        return new(key, label, TimeWeight, points, points > 0 ? FitOutcome.Partial : FitOutcome.NotMet, $"Only {days} days remain; your profile asks for at least {profile.MinimumResponseDays}.");
    }
}
