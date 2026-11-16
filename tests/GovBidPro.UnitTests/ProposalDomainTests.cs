using GovBidPro.Application.Proposals;
using GovBidPro.Domain.Common;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;

namespace GovBidPro.UnitTests;

public sealed class ProposalDomainTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);

    private static Proposal NewProposal() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "  Help desk  ", ResponseType.Rfp, At.AddDays(10), "Dec 1", "owner", At);

    [Fact]
    public void New_proposal_starts_in_draft_with_trimmed_title()
    {
        var proposal = NewProposal();
        Assert.Equal(ProposalStatus.Draft, proposal.Status);
        Assert.Equal("Help desk", proposal.Title);
        Assert.Equal("owner", proposal.CreatedByUserId);
        Assert.Throws<DomainException>(() => new Proposal(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('x', 301), ResponseType.Rfp, null, null, "owner", At));
    }

    [Fact]
    public void Status_change_returns_an_event_and_closed_proposals_are_frozen()
    {
        var proposal = NewProposal();
        var change = proposal.ChangeStatus(ProposalStatus.Cancelled, false, null, "No longer pursuing", "owner", At.AddHours(1));
        Assert.Equal(ProposalStatus.Draft, change.From);
        Assert.Equal(ProposalStatus.Cancelled, change.To);
        Assert.True(proposal.IsClosed);
        Assert.Throws<DomainException>(() => proposal.Update("New", ResponseType.Rfp, "owner", At));
    }

    [Fact]
    public void Exception_status_needs_evidence()
    {
        var details = new RequirementDetails("L.1", "Provide résumés.", null, null, true, null, null, null, null);
        var requirement = new ProposalRequirement(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), details, At);
        Assert.Equal(ComplianceStatus.NotStarted, requirement.Status);
        Assert.Throws<DomainException>(() => requirement.Update(details, ComplianceStatus.Exception, At));
        requirement.Update(details with { Evidence = "Key personnel waived per amendment 2" }, ComplianceStatus.Exception, At);
        Assert.Equal(ComplianceStatus.Exception, requirement.Status);
        Assert.Throws<DomainException>(() => requirement.Update(details with { Text = " " }, ComplianceStatus.NotStarted, At));
    }

    [Fact]
    public void Reviews_are_decided_once_by_the_assigned_reviewer()
    {
        var review = new ProposalReview(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "reviewer", "owner", null, At);
        Assert.Throws<DomainException>(() => review.Decide(ReviewOutcome.Approved, null, "someone-else", At));
        Assert.Throws<DomainException>(() => review.Decide(ReviewOutcome.ChangesRequested, null, "reviewer", At));
        Assert.Throws<DomainException>(() => review.Decide(ReviewOutcome.Superseded, "x", "reviewer", At));
        review.Decide(ReviewOutcome.Approved, "Looks good", "reviewer", At);
        Assert.Throws<DomainException>(() => review.Decide(ReviewOutcome.Approved, null, "reviewer", At));

        review.Supersede(At);
        Assert.Equal(ReviewOutcome.Approved, review.Outcome);
    }

    [Fact]
    public void Next_states_follow_the_lifecycle()
    {
        Assert.Equal([ProposalStatus.InProgress, ProposalStatus.Cancelled], ProposalLifecycle.NextStates(ProposalStatus.Draft).Order());
        Assert.Empty(ProposalLifecycle.NextStates(ProposalStatus.Won));
    }

    [Fact]
    public void Reference_comparer_uses_natural_number_order()
    {
        string[] refs = ["L.10", "M.1", "l.2", "L.2.1", "L.1"];
        Assert.Equal(["L.1", "l.2", "L.2.1", "L.10", "M.1"], refs.Order(ReferenceComparer.Instance));
    }

    [Fact]
    public void Stage_labels_fall_back_to_defaults_and_options_validate()
    {
        var options = new ProposalOptions { StageLabels = new() { ["InReview"] = "Pink team" } };
        Assert.Equal("Pink team", options.LabelFor(ProposalStatus.InReview));
        Assert.Equal("Draft", options.LabelFor(ProposalStatus.Draft));
        Assert.True(ProposalOptions.IsValid(options));
        Assert.False(ProposalOptions.IsValid(new ProposalOptions { MaxRequirementsPerProposal = 0 }));
        Assert.NotEmpty(options.SectionsFor(ResponseType.Rfp));
    }
}
