using GovBidPro.Application.Opportunities;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using GovBidPro.Domain.ValueObjects;

namespace GovBidPro.UnitTests;

public sealed class OpportunityFilterTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static Opportunity Make(string id, string title, string naics, string agency, SourceNoticeType type, string state)
    {
        var o = new Opportunity(Guid.NewGuid(), "fixture", id, false);
        var content = new OpportunityContent(title, null, null, agency, null, type, null, ResponseTypeMapper.Map(type, title, null), "v1", naics, null, null, state, "USA", Now, Now, ResponseDeadline.None, null, null);
        var (hash, snapshot) = OpportunityImportService.ComputeContentHash(content);
        o.ApplySourceContent(content, hash, snapshot, Guid.NewGuid(), Now);
        return o;
    }

    private static readonly List<Opportunity> Data =
    [
        Make("1", "Request for Proposal: Cloud migration", "541511", "Department of Examples", SourceNoticeType.Solicitation, "VA"),
        Make("2", "Sources Sought: Janitorial", "561720", "Example Agency", SourceNoticeType.SourcesSought, "TX"),
        Make("3", "RFQ: Furniture", "337214", "Other Agency", SourceNoticeType.CombinedSynopsisSolicitation, "VA"),
    ];

    [Fact]
    public void Filters_combine_with_and_semantics()
    {
        var result = OpportunityFilter.Apply(Data.AsQueryable(), new OpportunitySearchCriteria { PlaceOfPerformanceStates = ["va"], ResponseTypes = [ResponseType.Rfq] }).ToList();
        Assert.Equal("3", Assert.Single(result).ExternalId);
    }

    [Fact]
    public void Keyword_and_agency_matching_is_case_insensitive()
    {
        Assert.Equal("1", Assert.Single(OpportunityFilter.Apply(Data.AsQueryable(), new OpportunitySearchCriteria { Keywords = "CLOUD" })).ExternalId);
        Assert.Equal(2, OpportunityFilter.Apply(Data.AsQueryable(), new OpportunitySearchCriteria { Agencies = ["example"] }).Count());
    }

    [Fact]
    public void Notice_type_filter_is_independent_of_response_type()
    {
        var result = OpportunityFilter.Apply(Data.AsQueryable(), new OpportunitySearchCriteria { NoticeTypes = [SourceNoticeType.SourcesSought] }).ToList();
        Assert.Equal("2", Assert.Single(result).ExternalId);
    }
}
