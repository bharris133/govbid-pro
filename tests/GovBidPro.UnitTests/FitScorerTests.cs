using GovBidPro.Application.Assessments;
using GovBidPro.Domain.Common;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;

namespace GovBidPro.UnitTests;

/// <summary>P1.3: the fit score is rule-based and every point is explained.</summary>
public sealed class FitScorerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static CompanyFitProfile Profile() => new()
    {
        NaicsCodes = ["541511", "541512"],
        SetAsideQualifications = [SetAsideCategory.SmallBusiness, SetAsideCategory.Sdvosb],
        PreferredStates = ["VA", "MD"],
        PreferredAgencies = ["Veterans Affairs"],
        CapabilityKeywords = ["cloud", "migration", "cybersecurity"],
        MinimumResponseDays = 10,
    };

    private static OpportunityFacts Notice(string? naics = "541511", string? setAside = "Total Small Business Set-Aside (FAR 19.5)", string? state = "VA", string? agency = "Department of Veterans Affairs", int? dueInDays = 20, string title = "Cloud migration support", SourceNoticeType type = SourceNoticeType.Solicitation) =>
        new(title, "Migrate legacy systems.", agency, naics, setAside, state, type, dueInDays is { } d ? Now.AddDays(d) : null);

    [Fact]
    public void A_full_match_scores_100_and_explains_every_criterion()
    {
        var result = FitScorer.Score(Profile(), Notice(), Now);
        Assert.Equal(100, result.Score);
        Assert.Equal("Strong", result.Band);
        Assert.Equal(FitScorer.RuleVersion, result.RuleVersion);
        Assert.Equal(6, result.Criteria.Count);
        Assert.All(result.Criteria, c => Assert.Equal(FitOutcome.Met, c.Outcome));
        Assert.All(result.Criteria, c => Assert.False(string.IsNullOrWhiteSpace(c.Explanation)));
        Assert.Equal(100, result.Criteria.Sum(c => c.Weight));
        Assert.Contains(result.Cautions, c => c.Contains("not a win probability", StringComparison.Ordinal));
        Assert.Contains(result.Cautions, c => c.Contains("does not certify", StringComparison.Ordinal));
    }

    [Fact]
    public void Each_unmet_criterion_removes_exactly_its_weight()
    {
        var result = FitScorer.Score(Profile(), Notice(naics: "236220", setAside: "8(a) Set-Aside (FAR 19.8)", state: "TX", agency: "Department of Energy", dueInDays: 5, title: "Building repair"), Now);
        Assert.Equal(FitOutcome.NotMet, result.Criteria.Single(c => c.Key == "naics").Outcome);
        Assert.Equal(FitOutcome.NotMet, result.Criteria.Single(c => c.Key == "set_aside").Outcome);
        Assert.Contains("EightA", result.Criteria.Single(c => c.Key == "set_aside").Explanation, StringComparison.Ordinal);
        Assert.Equal(FitOutcome.NotMet, result.Criteria.Single(c => c.Key == "keywords").Outcome);
        var time = result.Criteria.Single(c => c.Key == "time");
        Assert.Equal(FitOutcome.Partial, time.Outcome);
        Assert.Equal(5, time.Points);
        Assert.Equal(5, result.Score);
        Assert.Equal("Weak", result.Band);
    }

    [Fact]
    public void Related_naics_and_a_single_keyword_earn_half_points()
    {
        var result = FitScorer.Score(Profile(), Notice(naics: "541519", title: "Cloud licenses"), Now);
        Assert.Equal(15, result.Criteria.Single(c => c.Key == "naics").Points);
        Assert.Equal(10, result.Criteria.Single(c => c.Key == "keywords").Points);
        Assert.Equal(75, result.Score);
    }

    [Fact]
    public void Criteria_without_data_are_not_evaluated_and_do_not_lower_the_score()
    {
        var profile = new CompanyFitProfile { NaicsCodes = ["541511"] };
        var result = FitScorer.Score(profile, Notice(setAside: null, dueInDays: null), Now);

        // Only NAICS (30) and "no set-aside" (20) could be judged; both are met.
        Assert.Equal(100, result.Score);
        Assert.Equal(4, result.Criteria.Count(c => c.Outcome == FitOutcome.NotEvaluated));
        Assert.Contains(result.Cautions, c => c.Contains("could not be evaluated", StringComparison.Ordinal));
    }

    [Fact]
    public void An_empty_profile_and_sparse_notice_still_explain_themselves()
    {
        var result = FitScorer.Score(new CompanyFitProfile(), new OpportunityFacts("Notice", null, null, null, "Local Area Set-Aside", null, SourceNoticeType.AwardNotice, null), Now);
        Assert.Null(result.Score);
        Assert.Equal("NotScored", result.Band);
        Assert.Contains(result.Cautions, c => c.Contains("was not recognized", StringComparison.Ordinal));
        Assert.Contains(result.Cautions, c => c.Contains("award notice", StringComparison.Ordinal));
    }

    [Fact]
    public void A_passed_deadline_scores_zero_for_time_and_is_flagged()
    {
        var result = FitScorer.Score(Profile(), Notice(dueInDays: -1), Now);
        Assert.Equal(0, result.Criteria.Single(c => c.Key == "time").Points);
        Assert.Contains("The response deadline has passed.", result.Cautions);
    }

    [Theory]
    [InlineData("Total Small Business Set-Aside (FAR 19.5)", SetAsideCategory.SmallBusiness)]
    [InlineData("Service-Disabled Veteran-Owned Small Business (SDVOSB) Set-Aside", SetAsideCategory.Sdvosb)]
    [InlineData("Veteran-Owned Small Business Set-Aside", SetAsideCategory.Vosb)]
    [InlineData("8(a) Sole Source (FAR 19.8)", SetAsideCategory.EightA)]
    [InlineData("Historically Underutilized Business (HUBZone) Set-Aside", SetAsideCategory.HubZone)]
    [InlineData("Economically Disadvantaged Women-Owned Small Business (EDWOSB)", SetAsideCategory.Edwosb)]
    [InlineData("Women-Owned Small Business (WOSB) Program Set-Aside", SetAsideCategory.Wosb)]
    [InlineData("Indian Small Business Economic Enterprise (ISBEE) Set-Aside", SetAsideCategory.IndianEconomicEnterprise)]
    public void Set_aside_text_is_classified_most_specific_first(string text, SetAsideCategory expected) =>
        Assert.Equal(expected, FitScorer.ClassifySetAside(text));

    [Fact]
    public void Edwosb_covers_wosb_and_sdvosb_covers_vosb_but_small_business_does_not_cover_sdvosb()
    {
        var edwosb = new CompanyFitProfile { SetAsideQualifications = [SetAsideCategory.Edwosb, SetAsideCategory.Sdvosb] };
        Assert.Equal(FitOutcome.Met, FitScorer.Score(edwosb, Notice(setAside: "Women-Owned Small Business"), Now).Criteria.Single(c => c.Key == "set_aside").Outcome);
        Assert.Equal(FitOutcome.Met, FitScorer.Score(edwosb, Notice(setAside: "Veteran-Owned Small Business"), Now).Criteria.Single(c => c.Key == "set_aside").Outcome);

        var small = new CompanyFitProfile { SetAsideQualifications = [SetAsideCategory.SmallBusiness] };
        Assert.Equal(FitOutcome.NotMet, FitScorer.Score(small, Notice(setAside: "SDVOSB Set-Aside"), Now).Criteria.Single(c => c.Key == "set_aside").Outcome);
    }

    [Fact]
    public void Profile_normalization_bounds_and_validates_input()
    {
        var normalized = new CompanyFitProfile { NaicsCodes = [" 541511 ", "541511", ""], PreferredStates = ["va", "Va"], CapabilityKeywords = [new string('x', 100)] }.Normalized();
        Assert.Equal(["541511"], normalized.NaicsCodes);
        Assert.Equal(["VA"], normalized.PreferredStates);
        Assert.Equal(60, normalized.CapabilityKeywords[0].Length);
        Assert.Throws<DomainException>(() => new CompanyFitProfile { NaicsCodes = ["54A"] }.Normalized());
        Assert.Throws<DomainException>(() => new CompanyFitProfile { MinimumResponseDays = 999 }.Normalized());
    }

    [Fact]
    public void A_go_or_no_go_decision_requires_a_rationale()
    {
        OpportunityAssessment Make(GoNoGoDecision decision, string? rationale) =>
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 1, 80, "Strong", "fit-v1", "[]", decision, rationale, "user", Now);

        Assert.Throws<DomainException>(() => Make(GoNoGoDecision.Go, " "));
        Assert.Throws<DomainException>(() => Make(GoNoGoDecision.NoGo, null));
        Assert.Null(Make(GoNoGoDecision.Watch, null).Rationale);
        Assert.Equal("Core capability.", Make(GoNoGoDecision.Go, " Core capability. ").Rationale);
    }
}
