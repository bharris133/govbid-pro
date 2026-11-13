using GovBidPro.Application.Security;
using GovBidPro.Domain.Enums;

namespace GovBidPro.UnitTests;

/// <summary>SEC-02: every classification has defined behavior at every processing boundary.</summary>
public sealed class ClassificationPolicyTests
{
    public static TheoryData<DataClassification, ProcessingPurpose> AllCombinations()
    {
        var data = new TheoryData<DataClassification, ProcessingPurpose>();
        foreach (var c in Enum.GetValues<DataClassification>())
        {
            foreach (var p in Enum.GetValues<ProcessingPurpose>())
            {
                data.Add(c, p);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void Restricted_and_prohibited_are_denied_for_every_purpose(DataClassification classification, ProcessingPurpose purpose)
    {
        var decision = ClassificationPolicy.Evaluate(classification, purpose, confidentialAiApproved: true);
        if (classification is DataClassification.RestrictedPendingReview or DataClassification.ProhibitedClassified)
        {
            Assert.False(decision.Allowed);
        }
        else if (purpose != ProcessingPurpose.AiAssist)
        {
            Assert.True(decision.Allowed);
        }
    }

    [Theory]
    [InlineData(DataClassification.Public, true)]
    [InlineData(DataClassification.BusinessInternal, false)]
    [InlineData(DataClassification.ConfidentialCommercial, false)]
    [InlineData(DataClassification.RestrictedPendingReview, false)]
    [InlineData(DataClassification.ProhibitedClassified, false)]
    public void Ai_eligibility_defaults_to_public_only(DataClassification classification, bool allowed)
    {
        Assert.Equal(allowed, ClassificationPolicy.Evaluate(classification, ProcessingPurpose.AiAssist, confidentialAiApproved: false).Allowed);
    }

    [Fact]
    public void Confidential_ai_requires_ai02_approval_flag()
    {
        var denied = ClassificationPolicy.Evaluate(DataClassification.ConfidentialCommercial, ProcessingPurpose.AiAssist, false);
        Assert.Equal("ai.confidential_requires_ai02_approval", denied.ReasonCode);
        Assert.True(ClassificationPolicy.Evaluate(DataClassification.ConfidentialCommercial, ProcessingPurpose.AiAssist, true).Allowed);
    }
}
