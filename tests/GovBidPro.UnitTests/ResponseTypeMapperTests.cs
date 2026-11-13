using GovBidPro.Application.Opportunities;
using GovBidPro.Domain.Enums;

namespace GovBidPro.UnitTests;

public sealed class ResponseTypeMapperTests
{
    [Theory]
    [InlineData(SourceNoticeType.SourcesSought, "Anything", null, ResponseType.SourcesSought)]
    [InlineData(SourceNoticeType.AwardNotice, "Request for Proposal award", null, ResponseType.NoResponseExpected)]
    [InlineData(SourceNoticeType.Solicitation, "Request for Proposal: Help desk", null, ResponseType.Rfp)]
    [InlineData(SourceNoticeType.CombinedSynopsisSolicitation, "RFQ: Furniture", null, ResponseType.Rfq)]
    [InlineData(SourceNoticeType.SpecialNotice, "Request for Information: Analytics", null, ResponseType.Rfi)]
    [InlineData(SourceNoticeType.Solicitation, "Software licenses", null, ResponseType.Undetermined)]
    [InlineData(SourceNoticeType.Solicitation, "RFI and RFP combined", null, ResponseType.Undetermined)]
    [InlineData(SourceNoticeType.Solicitation, "SURFPARK maintenance", null, ResponseType.Undetermined)]
    [InlineData(SourceNoticeType.SoleSource, "Request for Proposal", null, ResponseType.Undetermined)]
    [InlineData(SourceNoticeType.Unknown, "RFP", null, ResponseType.Undetermined)]
    public void Maps_only_explicit_wording_and_keeps_notice_type_separate(SourceNoticeType notice, string title, string? raw, ResponseType expected)
    {
        Assert.Equal(expected, ResponseTypeMapper.Map(notice, title, raw));
    }

    [Fact]
    public void Rule_version_is_declared()
    {
        Assert.False(string.IsNullOrWhiteSpace(ResponseTypeMapper.RuleVersion));
    }
}
