using GovBidPro.Domain.Common;
using GovBidPro.Domain.Entities;

namespace GovBidPro.UnitTests;

public sealed class ProposalLifecycleTests
{
    [Theory]
    [InlineData(ProposalStatus.Draft, ProposalStatus.InProgress)]
    [InlineData(ProposalStatus.InProgress, ProposalStatus.InReview)]
    [InlineData(ProposalStatus.InReview, ProposalStatus.InProgress)]
    [InlineData(ProposalStatus.SubmittedExternally, ProposalStatus.Won)]
    public void Allows_defined_transitions(ProposalStatus from, ProposalStatus to)
    {
        ProposalLifecycle.EnsureTransition(from, to, hasRecordedReviewerApproval: false, hasSubmissionEvidence: false);
    }

    [Theory]
    [InlineData(ProposalStatus.Draft, ProposalStatus.ReadyForSubmission)]
    [InlineData(ProposalStatus.Draft, ProposalStatus.SubmittedExternally)]
    [InlineData(ProposalStatus.Won, ProposalStatus.Lost)]
    [InlineData(ProposalStatus.Cancelled, ProposalStatus.Draft)]
    public void Rejects_undefined_transitions(ProposalStatus from, ProposalStatus to)
    {
        Assert.Throws<DomainException>(() => ProposalLifecycle.EnsureTransition(from, to, true, true));
    }

    [Fact]
    public void Approval_requires_recorded_reviewer()
    {
        Assert.Throws<DomainException>(() => ProposalLifecycle.EnsureTransition(ProposalStatus.InReview, ProposalStatus.Approved, false, false));
        ProposalLifecycle.EnsureTransition(ProposalStatus.InReview, ProposalStatus.Approved, true, false);
    }

    [Fact]
    public void External_submission_requires_user_evidence()
    {
        Assert.Throws<DomainException>(() => ProposalLifecycle.EnsureTransition(ProposalStatus.ReadyForSubmission, ProposalStatus.SubmittedExternally, true, false));
        ProposalLifecycle.EnsureTransition(ProposalStatus.ReadyForSubmission, ProposalStatus.SubmittedExternally, true, true);
    }
}
