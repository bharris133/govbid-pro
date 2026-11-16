using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Auditing;
using GovBidPro.Application.Common;
using GovBidPro.Application.Proposals;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using GovBidPro.Testing;
using Microsoft.EntityFrameworkCore;

namespace GovBidPro.IntegrationTests;

/// <summary>P1.4: responses from opportunities, compliance matrix, assignments, reviews and the gated lifecycle.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class ProposalTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private async Task<(TestWorld World, Guid Tenant, string Admin, Guid Opportunity)> SetupAsync()
    {
        var world = new TestWorld(await postgres.CreateMigratedDatabaseAsync(), new MutableTimeProvider(Start));
        var (tenant, admin) = await world.CreateCompanyAsync("Tenant A");
        await world.ImportAsync(world.Notice("P1", "Request for Proposal: Help desk modernization", dueInDays: 25));
        return (world, tenant, admin, await world.OpportunityIdAsync("P1"));
    }

    private static RequirementCommand Requirement(string reference, bool mandatory = true, ComplianceStatus status = ComplianceStatus.NotStarted, string? evidence = null, string? assignee = null) =>
        new(reference, $"The offeror shall address {reference}.", "Section L", "Technical", mandatory, null, null, assignee, evidence, status);

    [Fact]
    public async Task Creating_a_response_snapshots_the_opportunity_and_adds_default_sections()
    {
        var (world, tenant, admin, opportunity) = await SetupAsync();
        var actor = TestWorld.Tenant(tenant, admin);
        var audit = new RecordingAuditLog();
        await using var db = world.Context(actor);
        var service = world.Proposals(db, actor, audit);

        var created = await service.CreateAsync(new CreateProposalCommand(opportunity, null, null), CancellationToken.None);

        Assert.Equal("Request for Proposal: Help desk modernization", created.Title);
        Assert.Equal(ProposalStatus.Draft, created.Status);
        Assert.Equal("Draft", created.StatusLabel);
        Assert.Equal(admin, created.OwnerUserId);
        Assert.Equal(Start.AddDays(25), created.DueAt);
        Assert.Equal(world.Proposal.SectionsFor(created.ResponseType).Count, created.Sections.Count);
        Assert.Contains(created.NextStages, s => s.Status == ProposalStatus.InProgress && s.Allowed);
        Assert.Contains(audit.Entries, e => e.Action == AuditActions.ProposalCreated);

        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(new CreateProposalCommand(opportunity, "Second", null), CancellationToken.None));
        Assert.Contains(created.Id.ToString(), duplicate.Message, StringComparison.Ordinal);

        var listed = await service.ListAsync(includeClosed: false, CancellationToken.None);
        Assert.Equal(created.Id, Assert.Single(listed).Id);
    }

    [Fact]
    public async Task Reviewers_cannot_create_and_other_tenants_get_not_found()
    {
        var (world, tenant, admin, opportunity) = await SetupAsync();
        var actor = TestWorld.Tenant(tenant, admin);
        Guid proposalId;
        await using (var db = world.Context(actor))
        {
            proposalId = (await world.Proposals(db, actor).CreateAsync(new CreateProposalCommand(opportunity, null, null), CancellationToken.None)).Id;
        }

        var reviewerOnlyId = await world.AddMemberAsync(tenant, CompanyRole.Reviewer);
        var reviewerOnly = TestWorld.Tenant(tenant, reviewerOnlyId, CompanyRole.Reviewer);
        await using (var db = world.Context(reviewerOnly))
        {
            var service = world.Proposals(db, reviewerOnly);
            await Assert.ThrowsAsync<PolicyDeniedException>(() => service.CreateAsync(new CreateProposalCommand(opportunity, null, null), CancellationToken.None));
            Assert.Equal(proposalId, (await service.GetAsync(proposalId, CancellationToken.None)).Id);
            await Assert.ThrowsAsync<PolicyDeniedException>(() => service.AddRequirementAsync(proposalId, Requirement("L.1"), CancellationToken.None));
        }

        var (otherTenant, otherAdmin) = await world.CreateCompanyAsync("Tenant B");
        var other = TestWorld.Tenant(otherTenant, otherAdmin);
        var audit = new RecordingAuditLog();
        await using (var db = world.Context(other))
        {
            var service = world.Proposals(db, other, audit);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.GetAsync(proposalId, CancellationToken.None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.AddRequirementAsync(proposalId, Requirement("L.1"), CancellationToken.None));
            Assert.Empty(await service.ListAsync(includeClosed: true, CancellationToken.None));
            Assert.Contains(audit.Entries, e => e.Action == AuditActions.AccessDenied && e.Outcome == AuditOutcome.Denied);

            // Tenant B can still start its own response to the same public opportunity.
            Assert.NotEqual(proposalId, (await service.CreateAsync(new CreateProposalCommand(opportunity, null, null), CancellationToken.None)).Id);
        }
    }

    [Fact]
    public async Task Compliance_matrix_validates_references_assignees_and_exceptions()
    {
        var (world, tenant, admin, opportunity) = await SetupAsync();
        var writerId = await world.AddMemberAsync(tenant, CompanyRole.ProposalWriter);
        var (otherTenant, outsider) = await world.CreateCompanyAsync("Tenant B");
        Assert.NotEqual(tenant, otherTenant);
        var actor = TestWorld.Tenant(tenant, admin);
        await using var db = world.Context(actor);
        var service = world.Proposals(db, actor);
        var proposal = await service.CreateAsync(new CreateProposalCommand(opportunity, null, null), CancellationToken.None);

        var l10 = await service.AddRequirementAsync(proposal.Id, Requirement("L.10", assignee: writerId), CancellationToken.None);
        await service.AddRequirementAsync(proposal.Id, Requirement("L.2", mandatory: false), CancellationToken.None);
        await service.AddRequirementAsync(proposal.Id, Requirement("M.1"), CancellationToken.None);
        Assert.EndsWith("@example.test", l10.Assignee, StringComparison.Ordinal);

        await Assert.ThrowsAsync<ConflictException>(() => service.AddRequirementAsync(proposal.Id, Requirement("l.10"), CancellationToken.None));
        var notMember = await Assert.ThrowsAsync<RequestValidationException>(() => service.AddRequirementAsync(proposal.Id, Requirement("L.3", assignee: outsider), CancellationToken.None));
        Assert.Equal("requirement.assignee_not_member", notMember.ReasonCode);
        await Assert.ThrowsAsync<RequestValidationException>(() => service.UpdateRequirementAsync(proposal.Id, l10.Id, Requirement("L.10", status: ComplianceStatus.Exception), CancellationToken.None));

        var updated = await service.UpdateRequirementAsync(proposal.Id, l10.Id, Requirement("L.10", status: ComplianceStatus.Exception, evidence: "Not applicable to on-site work; see cover letter."), CancellationToken.None);
        Assert.Equal(ComplianceStatus.Exception, updated.Status);

        var detail = await service.GetAsync(proposal.Id, CancellationToken.None);
        Assert.Equal(["L.2", "L.10", "M.1"], detail.Requirements.Select(r => r.Reference));
        Assert.Equal(new ComplianceSummaryDto(3, 2, 1, 0, 1, 0), detail.Compliance);

        await service.DeleteRequirementAsync(proposal.Id, l10.Id, CancellationToken.None);
        Assert.Equal(2, (await service.GetAsync(proposal.Id, CancellationToken.None)).Requirements.Count);
    }

    [Fact]
    public async Task Approval_needs_an_assigned_reviewer_and_mandatory_items_must_be_resolved()
    {
        var (world, tenant, owner, opportunity) = await SetupAsync();
        var reviewerId = await world.AddMemberAsync(tenant, CompanyRole.Reviewer);
        var writerId = await world.AddMemberAsync(tenant, CompanyRole.ProposalWriter);
        var notifier = new RecordingProposalNotifier();
        var ownerCtx = TestWorld.Tenant(tenant, owner, CompanyRole.CompanyAdmin);
        Guid proposalId;
        Guid requirementId;
        await using (var db = world.Context(ownerCtx))
        {
            var service = world.Proposals(db, ownerCtx, notifier: notifier);
            var proposal = await service.CreateAsync(new CreateProposalCommand(opportunity, null, null), CancellationToken.None);
            proposalId = proposal.Id;
            requirementId = (await service.AddRequirementAsync(proposalId, Requirement("L.1"), CancellationToken.None)).Id;
            await service.ChangeStatusAsync(proposalId, new ChangeStatusCommand(ProposalStatus.InProgress, null, null), CancellationToken.None);

            await Assert.ThrowsAsync<ConflictException>(() => service.RequestReviewAsync(proposalId, new RequestReviewCommand(reviewerId, null), CancellationToken.None));
        }

        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await using (var db = world.Context(ownerCtx))
        {
            var service = world.Proposals(db, ownerCtx, notifier: notifier);
            await service.ChangeStatusAsync(proposalId, new ChangeStatusCommand(ProposalStatus.InReview, "Ready for a look", null), CancellationToken.None);
            var self = await Assert.ThrowsAsync<RequestValidationException>(() => service.RequestReviewAsync(proposalId, new RequestReviewCommand(owner, null), CancellationToken.None));
            Assert.Equal("review.self_review", self.ReasonCode);

            var blocked = await Assert.ThrowsAsync<ConflictException>(() => service.ChangeStatusAsync(proposalId, new ChangeStatusCommand(ProposalStatus.Approved, null, null), CancellationToken.None));
            Assert.Contains("reviewer must approve", blocked.Message, StringComparison.Ordinal);

            var detail = await service.RequestReviewAsync(proposalId, new RequestReviewCommand(reviewerId, "Check pricing volume"), CancellationToken.None);
            Assert.Equal(ReviewOutcome.Requested, Assert.Single(detail.Reviews).Outcome);
            Assert.Equal("proposal.review_pending", detail.NextStages.Single(s => s.Status == ProposalStatus.Approved).BlockedReason);
            await Assert.ThrowsAsync<ConflictException>(() => service.RequestReviewAsync(proposalId, new RequestReviewCommand(reviewerId, null), CancellationToken.None));
            Assert.Equal(reviewerId, Assert.Single(notifier.ReviewRequests));
        }

        var reviewId = await ReviewIdAsync(world, ownerCtx, proposalId);

        // Only the assigned reviewer can decide.
        var writer = TestWorld.Tenant(tenant, writerId, CompanyRole.ProposalWriter);
        await using (var db = world.Context(writer))
        {
            await Assert.ThrowsAsync<PolicyDeniedException>(() => world.Proposals(db, writer).DecideReviewAsync(proposalId, reviewId, new DecideReviewCommand(ReviewOutcome.Approved, null), CancellationToken.None));
        }

        var reviewer = TestWorld.Tenant(tenant, reviewerId, CompanyRole.Reviewer);
        await using (var db = world.Context(reviewer))
        {
            var service = world.Proposals(db, reviewer);
            await Assert.ThrowsAsync<RequestValidationException>(() => service.DecideReviewAsync(proposalId, reviewId, new DecideReviewCommand(ReviewOutcome.ChangesRequested, " "), CancellationToken.None));
            var decided = await service.DecideReviewAsync(proposalId, reviewId, new DecideReviewCommand(ReviewOutcome.ChangesRequested, "Pricing assumptions missing"), CancellationToken.None);
            Assert.Equal(ReviewOutcome.ChangesRequested, Assert.Single(decided.Reviews).Outcome);
            Assert.Equal("role.insufficient", decided.NextStages.Single(s => s.Status == ProposalStatus.Approved).BlockedReason);
        }

        await using (var db = world.Context(ownerCtx))
        {
            var detail = await world.Proposals(db, ownerCtx).GetAsync(proposalId, CancellationToken.None);
            Assert.Equal("proposal.review_changes_requested", detail.NextStages.Single(s => s.Status == ProposalStatus.Approved).BlockedReason);
        }

        // Second round: back to work, then review again and approve.
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await using (var db = world.Context(ownerCtx))
        {
            var service = world.Proposals(db, ownerCtx);
            await service.ChangeStatusAsync(proposalId, new ChangeStatusCommand(ProposalStatus.InProgress, "Addressing pricing", null), CancellationToken.None);
            world.Clock.Advance(TimeSpan.FromMinutes(1));
            await service.ChangeStatusAsync(proposalId, new ChangeStatusCommand(ProposalStatus.InReview, null, null), CancellationToken.None);
            await service.RequestReviewAsync(proposalId, new RequestReviewCommand(reviewerId, null), CancellationToken.None);
        }

        var secondReview = await ReviewIdAsync(world, ownerCtx, proposalId);
        await using (var db = world.Context(reviewer))
        {
            await world.Proposals(db, reviewer).DecideReviewAsync(proposalId, secondReview, new DecideReviewCommand(ReviewOutcome.Approved, null), CancellationToken.None);
        }

        await using (var db = world.Context(writer))
        {
            var denied = await Assert.ThrowsAsync<PolicyDeniedException>(() => world.Proposals(db, writer).ChangeStatusAsync(proposalId, new ChangeStatusCommand(ProposalStatus.Approved, null, null), CancellationToken.None));
            Assert.Equal("role.insufficient", denied.ReasonCode);
        }

        await using (var db = world.Context(ownerCtx))
        {
            var service = world.Proposals(db, ownerCtx);
            var approved = await service.ChangeStatusAsync(proposalId, new ChangeStatusCommand(ProposalStatus.Approved, null, null), CancellationToken.None);
            Assert.Equal(ProposalStatus.Approved, approved.Status);
            Assert.Equal("proposal.mandatory_requirements_open", approved.NextStages.Single(s => s.Status == ProposalStatus.ReadyForSubmission).BlockedReason);
            await Assert.ThrowsAsync<ConflictException>(() => service.ChangeStatusAsync(proposalId, new ChangeStatusCommand(ProposalStatus.ReadyForSubmission, null, null), CancellationToken.None));

            await service.UpdateRequirementAsync(proposalId, requirementId, Requirement("L.1", status: ComplianceStatus.Compliant), CancellationToken.None);
            await service.ChangeStatusAsync(proposalId, new ChangeStatusCommand(ProposalStatus.ReadyForSubmission, null, null), CancellationToken.None);
            await Assert.ThrowsAsync<RequestValidationException>(() => service.ChangeStatusAsync(proposalId, new ChangeStatusCommand(ProposalStatus.SubmittedExternally, null, null), CancellationToken.None));
            var submitted = await service.ChangeStatusAsync(proposalId, new ChangeStatusCommand(ProposalStatus.SubmittedExternally, null, "Emailed to the contracting officer, confirmation #1234"), CancellationToken.None);
            Assert.Equal("Emailed to the contracting officer, confirmation #1234", submitted.SubmissionEvidence);
            Assert.Equal(7, submitted.History.Count);
            Assert.Equal(ReviewOutcome.ChangesRequested, submitted.Reviews.Single(r => r.Id == reviewId).Outcome);
            Assert.True(submitted.Reviews.Single(r => r.Id == secondReview).CurrentRound);

            var won = await service.ChangeStatusAsync(proposalId, new ChangeStatusCommand(ProposalStatus.Won, null, null), CancellationToken.None);
            Assert.Empty(won.NextStages);
            await Assert.ThrowsAsync<ConflictException>(() => service.AddRequirementAsync(proposalId, Requirement("L.9"), CancellationToken.None));
            Assert.Empty(await service.ListAsync(includeClosed: false, CancellationToken.None));
        }
    }

    [Fact]
    public async Task Moving_back_to_work_supersedes_pending_reviews()
    {
        var (world, tenant, owner, opportunity) = await SetupAsync();
        var reviewerId = await world.AddMemberAsync(tenant, CompanyRole.Reviewer);
        var ownerCtx = TestWorld.Tenant(tenant, owner);
        await using var db = world.Context(ownerCtx);
        var service = world.Proposals(db, ownerCtx);
        var proposal = await service.CreateAsync(new CreateProposalCommand(opportunity, null, null), CancellationToken.None);
        await service.ChangeStatusAsync(proposal.Id, new ChangeStatusCommand(ProposalStatus.InProgress, null, null), CancellationToken.None);
        await service.ChangeStatusAsync(proposal.Id, new ChangeStatusCommand(ProposalStatus.InReview, null, null), CancellationToken.None);
        await service.RequestReviewAsync(proposal.Id, new RequestReviewCommand(reviewerId, null), CancellationToken.None);

        var back = await service.ChangeStatusAsync(proposal.Id, new ChangeStatusCommand(ProposalStatus.InProgress, null, null), CancellationToken.None);

        Assert.Equal(ReviewOutcome.Superseded, Assert.Single(back.Reviews).Outcome);
        Assert.Equal(3, await db.ProposalEvents.CountAsync());
    }

    [Fact]
    public async Task Sections_and_owner_changes_only_accept_active_members()
    {
        var (world, tenant, owner, opportunity) = await SetupAsync();
        var writerId = await world.AddMemberAsync(tenant, CompanyRole.ProposalWriter);
        var (_, outsider) = await world.CreateCompanyAsync("Tenant B");
        var notifier = new RecordingProposalNotifier();
        var ownerCtx = TestWorld.Tenant(tenant, owner);
        await using var db = world.Context(ownerCtx);
        var service = world.Proposals(db, ownerCtx, notifier: notifier);
        var proposal = await service.CreateAsync(new CreateProposalCommand(opportunity, "Help desk bid", ResponseType.Rfq), CancellationToken.None);
        Assert.Equal(ResponseType.Rfq, proposal.ResponseType);

        var section = proposal.Sections[0];
        var assigned = await service.UpdateSectionAsync(proposal.Id, section.Id, new SectionCommand(section.Title, writerId, false), CancellationToken.None);
        Assert.Equal(writerId, assigned.Sections[0].AssigneeUserId);
        Assert.Contains(writerId, notifier.Assignments);
        await Assert.ThrowsAsync<RequestValidationException>(() => service.UpdateSectionAsync(proposal.Id, section.Id, new SectionCommand(section.Title, outsider, false), CancellationToken.None));

        var added = await service.AddSectionAsync(proposal.Id, new SectionCommand("Transition plan", null, false), CancellationToken.None);
        Assert.Equal("Transition plan", added.Sections[^1].Title);

        await Assert.ThrowsAsync<RequestValidationException>(() => service.UpdateAsync(proposal.Id, new UpdateProposalCommand("Help desk bid", ResponseType.Rfq, outsider), CancellationToken.None));
        var handedOver = await service.UpdateAsync(proposal.Id, new UpdateProposalCommand("Help desk bid v2", ResponseType.Rfq, writerId), CancellationToken.None);
        Assert.Equal(writerId, handedOver.OwnerUserId);
        Assert.Equal("Help desk bid v2", handedOver.Title);
    }

    private static async Task<Guid> ReviewIdAsync(TestWorld world, TenantContextHolder ctx, Guid proposalId)
    {
        await using var db = world.Context(ctx);
        return await db.ProposalReviews.Where(r => r.ProposalId == proposalId).OrderByDescending(r => r.RequestedAt).Select(r => r.Id).FirstAsync();
    }
}

internal sealed class RecordingProposalNotifier : IProposalNotifier
{
    public List<string> Assignments { get; } = [];

    public List<string> ReviewRequests { get; } = [];

    public Task AssignedAsync(Proposal proposal, string assigneeUserId, string what, CancellationToken cancellationToken)
    {
        Assignments.Add(assigneeUserId);
        return Task.CompletedTask;
    }

    public Task ReviewRequestedAsync(Proposal proposal, ProposalReview review, CancellationToken cancellationToken)
    {
        ReviewRequests.Add(review.ReviewerUserId);
        return Task.CompletedTask;
    }

    public Task ReviewDecidedAsync(Proposal proposal, ProposalReview review, CancellationToken cancellationToken) => Task.CompletedTask;
}
