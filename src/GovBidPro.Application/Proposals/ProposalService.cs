using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Auditing;
using GovBidPro.Application.Common;
using GovBidPro.Application.Companies;
using GovBidPro.Domain.Common;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GovBidPro.Application.Proposals;

/// <summary>
/// Proposals and their compliance matrix, sections, reviews and lifecycle (handoff §7, P1.4).
/// Every query filters by the resolved tenant; other companies' ids produce 404 and an audit entry.
/// </summary>
public sealed class ProposalService(
    IAppDbContext db,
    ITenantContext tenant,
    IAuditLog audit,
    IUserDirectory users,
    IProposalNotifier notifier,
    IOptions<ProposalOptions> options,
    TimeProvider clock)
{
    private ProposalOptions Settings => options.Value;

    public async Task<IReadOnlyList<ProposalSummaryDto>> ListAsync(bool includeClosed, CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        var query = db.Proposals.AsNoTracking().Where(p => p.TenantId == tenantId);
        if (!includeClosed)
        {
            query = query.Where(p => p.Status != ProposalStatus.Won && p.Status != ProposalStatus.Lost && p.Status != ProposalStatus.Cancelled);
        }

        var rows = await (from p in query
                          join o in db.Opportunities.AsNoTracking() on p.OpportunityId equals o.Id
                          orderby p.UpdatedAt descending
                          select new { Proposal = p, OpportunityTitle = o.Title, Due = o.Deadline.DueAt })
            .Take(500)
            .ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.Proposal.Id).ToList();
        var requirements = await db.ProposalRequirements.AsNoTracking()
            .Where(r => r.TenantId == tenantId && ids.Contains(r.ProposalId))
            .Select(r => new { r.ProposalId, r.Mandatory, r.Status })
            .ToListAsync(cancellationToken);
        var byProposal = requirements.ToLookup(r => r.ProposalId, r => (r.Mandatory, r.Status));
        var names = await users.GetAsync(rows.Select(r => r.Proposal.OwnerUserId).ToList(), cancellationToken);
        return rows.Select(r => new ProposalSummaryDto(
            r.Proposal.Id, r.Proposal.Title, r.Proposal.OpportunityId, r.OpportunityTitle, r.Proposal.ResponseType, r.Proposal.Status,
            Settings.LabelFor(r.Proposal.Status), names.NameOf(r.Proposal.OwnerUserId), r.Due ?? r.Proposal.DueAtSnapshot,
            Summarize(byProposal[r.Proposal.Id]), r.Proposal.UpdatedAt)).ToList();
    }

    public async Task<ProposalDetailDto> CreateAsync(CreateProposalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        tenant.RequireRole(CompanyRole.ProposalWriter);
        var tenantId = tenant.TenantId;
        var userId = tenant.RequireUser();
        var opportunity = await db.Opportunities.AsNoTracking().SingleOrDefaultAsync(o => o.Id == command.OpportunityId, cancellationToken)
            ?? throw new ResourceNotFoundException();

        var existing = await db.Proposals.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.OpportunityId == opportunity.Id && p.Status != ProposalStatus.Won && p.Status != ProposalStatus.Lost && p.Status != ProposalStatus.Cancelled)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            throw new ConflictException($"Your company already has an open proposal for this opportunity ({existing}).");
        }

        var responseType = command.ResponseType ?? opportunity.ResponseType;
        if (!Enum.IsDefined(responseType))
        {
            throw new RequestValidationException("proposal.invalid", "Unknown response type.");
        }

        var now = clock.GetUtcNow();
        Proposal proposal;
        try
        {
            proposal = new Proposal(
                Guid.NewGuid(), tenantId, opportunity.Id, opportunity.RevisionNumber, string.IsNullOrWhiteSpace(command.Title) ? opportunity.Title : command.Title,
                responseType, opportunity.Deadline.DueAt, opportunity.Deadline.OriginalText, userId, now);
        }
        catch (DomainException ex)
        {
            throw new RequestValidationException("proposal.invalid", ex.Message);
        }

        db.Proposals.Add(proposal);
        var order = 0;
        foreach (var title in Settings.SectionsFor(responseType))
        {
            db.ProposalSections.Add(new ProposalSection(Guid.NewGuid(), tenantId, proposal.Id, ++order, title, null));
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.ProposalCreated, "proposal", proposal.Id.ToString(), AuditOutcome.Success, "ok", cancellationToken);
        return await GetAsync(proposal.Id, cancellationToken);
    }

    public async Task<ProposalDetailDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        var proposal = await FindAsync(id, tracked: false, cancellationToken);
        var opportunity = await db.Opportunities.AsNoTracking().SingleAsync(o => o.Id == proposal.OpportunityId, cancellationToken);
        var sections = await db.ProposalSections.AsNoTracking().Where(s => s.TenantId == tenantId && s.ProposalId == id).OrderBy(s => s.Order).ToListAsync(cancellationToken);
        var requirements = await db.ProposalRequirements.AsNoTracking().Where(r => r.TenantId == tenantId && r.ProposalId == id).ToListAsync(cancellationToken);
        var reviews = await db.ProposalReviews.AsNoTracking().Where(r => r.TenantId == tenantId && r.ProposalId == id).OrderByDescending(r => r.RequestedAt).ToListAsync(cancellationToken);
        var events = await db.ProposalEvents.AsNoTracking().Where(e => e.TenantId == tenantId && e.ProposalId == id).OrderByDescending(e => e.OccurredAt).ToListAsync(cancellationToken);
        var roundStart = events.Where(e => e.To == ProposalStatus.InReview).Select(e => (DateTimeOffset?)e.OccurredAt).FirstOrDefault();

        var names = await users.GetAsync(
            new[] { proposal.OwnerUserId }
                .Concat(sections.Select(s => s.AssigneeUserId))
                .Concat(requirements.Select(r => r.AssigneeUserId))
                .Concat(reviews.SelectMany(r => new[] { r.ReviewerUserId, r.RequestedByUserId }))
                .Concat(events.Select(e => e.ActorUserId))
                .OfType<string>().Distinct(StringComparer.Ordinal).ToList(),
            cancellationToken);
        var sectionTitles = sections.ToDictionary(s => s.Id, s => s.Title);
        var compliance = Summarize(requirements.Select(r => (r.Mandatory, r.Status)));
        var approval = ApprovalState(reviews, roundStart);

        var warnings = new List<string>();
        if (proposal.ResponseType == ResponseType.Undetermined)
        {
            warnings.Add("The response type is undetermined. Choose RFP, RFQ, RFI or Sources Sought from the solicitation.");
        }

        if (opportunity.RevisionNumber > proposal.OpportunityRevisionNumber)
        {
            warnings.Add($"The notice has been amended since this proposal started (now revision {opportunity.RevisionNumber}). Review the changes and update the compliance matrix.");
        }

        if (opportunity.Deadline.DueAt is { } due && due < clock.GetUtcNow() && !proposal.IsClosed && proposal.Status != ProposalStatus.SubmittedExternally)
        {
            warnings.Add("The response deadline in the notice has passed.");
        }

        if (!opportunity.IsLiveSource)
        {
            warnings.Add("This proposal is for demo fixture data, not a live government notice.");
        }

        warnings.Add("Compliance statuses are your team's own tracking. GovBid Pro does not determine compliance or submit responses; follow the solicitation's instructions.");

        var next = ProposalLifecycle.NextStates(proposal.Status)
            .Select(to => new NextStageDto(to, Settings.LabelFor(to), BlockedReason(proposal, to, approval, compliance) is null, BlockedReason(proposal, to, approval, compliance)))
            .ToList();

        return new ProposalDetailDto(
            proposal.Id, proposal.Title, proposal.ResponseType, proposal.Status, Settings.LabelFor(proposal.Status), proposal.OwnerUserId, names.NameOf(proposal.OwnerUserId),
            opportunity.Id, opportunity.Title, opportunity.SolicitationNumber, opportunity.IsLiveSource, opportunity.SourceUrl, opportunity.SubmissionInstructions,
            opportunity.Deadline.DueAt ?? proposal.DueAtSnapshot, opportunity.Deadline.OriginalText ?? proposal.DueTextSnapshot, proposal.SubmissionEvidence,
            proposal.CreatedAt, proposal.UpdatedAt, warnings,
            Enum.GetValues<ProposalStatus>().Select(s => new StageDto(s, Settings.LabelFor(s))).ToList(),
            next, compliance,
            sections.Select(s => new SectionDto(s.Id, s.Order, s.Title, s.AssigneeUserId, s.AssigneeUserId is null ? null : names.NameOf(s.AssigneeUserId), s.IsComplete)).ToList(),
            requirements
                .OrderBy(r => r.Reference, ReferenceComparer.Instance)
                .Select(r => new RequirementDto(
                    r.Id, r.Reference, r.Text, r.SourceLocator, r.Category, r.Mandatory, r.SectionId, r.SectionId is { } sid && sectionTitles.TryGetValue(sid, out var t) ? t : null,
                    r.ResponseLocation, r.AssigneeUserId, r.AssigneeUserId is null ? null : names.NameOf(r.AssigneeUserId), r.Evidence, r.Status, r.UpdatedAt))
                .ToList(),
            reviews.Select(r => new ReviewDto(r.Id, r.ReviewerUserId, names.NameOf(r.ReviewerUserId), names.NameOf(r.RequestedByUserId), r.Instructions, r.RequestedAt, r.Outcome, r.Comment, r.DecidedAt, roundStart is { } start && r.RequestedAt >= start)).ToList(),
            events.Select(e => new ProposalEventDto(e.From, e.To, Settings.LabelFor(e.From), Settings.LabelFor(e.To), names.NameOf(e.ActorUserId), e.Note, e.OccurredAt)).ToList());
    }

    public async Task<ProposalDetailDto> UpdateAsync(Guid id, UpdateProposalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        tenant.RequireRole(CompanyRole.ProposalWriter);
        var proposal = await FindAsync(id, tracked: true, cancellationToken);
        if (!Enum.IsDefined(command.ResponseType))
        {
            throw new RequestValidationException("proposal.invalid", "Unknown response type.");
        }

        await db.EnsureActiveMemberAsync(proposal.TenantId, command.OwnerUserId, "proposal.owner_not_member", cancellationToken);
        var ownerChanged = !string.Equals(proposal.OwnerUserId, command.OwnerUserId, StringComparison.Ordinal);
        Mutate(() => proposal.Update(command.Title, command.ResponseType, command.OwnerUserId, clock.GetUtcNow()));
        await SaveAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.ProposalUpdated, "proposal", id.ToString(), AuditOutcome.Success, ownerChanged ? "owner_changed" : "ok", cancellationToken);
        if (ownerChanged)
        {
            await notifier.AssignedAsync(proposal, command.OwnerUserId, "proposal owner", cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    public async Task<ProposalDetailDto> ChangeStatusAsync(Guid id, ChangeStatusCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        tenant.RequireRole(CompanyRole.ProposalWriter);
        var userId = tenant.RequireUser();
        var proposal = await FindAsync(id, tracked: true, cancellationToken);
        if (!Enum.IsDefined(command.To))
        {
            throw new RequestValidationException("proposal.invalid", "Unknown status.");
        }

        var reviews = await db.ProposalReviews.Where(r => r.TenantId == proposal.TenantId && r.ProposalId == id).ToListAsync(cancellationToken);
        var roundStart = await db.ProposalEvents.AsNoTracking()
            .Where(e => e.TenantId == proposal.TenantId && e.ProposalId == id && e.To == ProposalStatus.InReview)
            .OrderByDescending(e => e.OccurredAt).Select(e => (DateTimeOffset?)e.OccurredAt).FirstOrDefaultAsync(cancellationToken);
        var requirements = await db.ProposalRequirements.AsNoTracking().Where(r => r.TenantId == proposal.TenantId && r.ProposalId == id).Select(r => new { r.Mandatory, r.Status }).ToListAsync(cancellationToken);
        var approval = ApprovalState(reviews, roundStart);
        var blocked = BlockedReason(proposal, command.To, approval, Summarize(requirements.Select(r => (r.Mandatory, r.Status))));
        if (blocked is not null)
        {
            await audit.RecordAsync(AuditActions.ProposalStatusChanged, "proposal", id.ToString(), AuditOutcome.Denied, blocked, cancellationToken);
            throw blocked == "role.insufficient" ? new PolicyDeniedException(blocked) : new ConflictException(BlockedMessage(blocked));
        }

        var now = clock.GetUtcNow();
        var change = Mutate(() => proposal.ChangeStatus(command.To, approval.Approved, command.SubmissionEvidence, command.Note, userId, now));
        db.ProposalEvents.Add(change);
        if (command.To == ProposalStatus.InProgress)
        {
            foreach (var pending in reviews)
            {
                pending.Supersede(now);
            }
        }

        await SaveAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.ProposalStatusChanged, "proposal", id.ToString(), AuditOutcome.Success, $"{change.From}->{change.To}", cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<ProposalDetailDto> AddSectionAsync(Guid id, SectionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        tenant.RequireRole(CompanyRole.ProposalWriter);
        var proposal = await FindOpenAsync(id, cancellationToken);
        await db.EnsureActiveMemberAsync(proposal.TenantId, command.AssigneeUserId, "proposal.assignee_not_member", cancellationToken);
        var count = await db.ProposalSections.CountAsync(s => s.TenantId == proposal.TenantId && s.ProposalId == id, cancellationToken);
        if (count >= 100)
        {
            throw new RequestValidationException("proposal.section_limit", "A proposal can have at most 100 sections.");
        }

        var section = Mutate(() => new ProposalSection(Guid.NewGuid(), proposal.TenantId, id, count + 1, command.Title, command.AssigneeUserId));
        db.ProposalSections.Add(section);
        Touch(proposal);
        await SaveAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.ProposalSectionChanged, "proposal_section", section.Id.ToString(), AuditOutcome.Success, "created", cancellationToken);
        if (command.AssigneeUserId is not null)
        {
            await notifier.AssignedAsync(proposal, command.AssigneeUserId, $"section “{section.Title}”", cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    public async Task<ProposalDetailDto> UpdateSectionAsync(Guid id, Guid sectionId, SectionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        tenant.RequireRole(CompanyRole.ProposalWriter);
        var proposal = await FindOpenAsync(id, cancellationToken);
        var section = await db.ProposalSections.SingleOrDefaultAsync(s => s.Id == sectionId && s.TenantId == proposal.TenantId && s.ProposalId == id, cancellationToken)
            ?? throw new ResourceNotFoundException();
        await db.EnsureActiveMemberAsync(proposal.TenantId, command.AssigneeUserId, "proposal.assignee_not_member", cancellationToken);
        var newAssignee = command.AssigneeUserId is not null && !string.Equals(command.AssigneeUserId, section.AssigneeUserId, StringComparison.Ordinal);
        Mutate(() => section.Update(command.Title, command.AssigneeUserId, command.IsComplete));
        Touch(proposal);
        await SaveAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.ProposalSectionChanged, "proposal_section", sectionId.ToString(), AuditOutcome.Success, "updated", cancellationToken);
        if (newAssignee)
        {
            await notifier.AssignedAsync(proposal, command.AssigneeUserId!, $"section “{section.Title}”", cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    public async Task<RequirementDto> AddRequirementAsync(Guid id, RequirementCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        tenant.RequireRole(CompanyRole.ProposalWriter);
        var proposal = await FindOpenAsync(id, cancellationToken);
        var count = await db.ProposalRequirements.CountAsync(r => r.TenantId == proposal.TenantId && r.ProposalId == id, cancellationToken);
        if (count >= Settings.MaxRequirementsPerProposal)
        {
            throw new RequestValidationException("requirement.limit", "This proposal has reached the requirement limit.");
        }

        await ValidateRequirementLinksAsync(proposal, command, cancellationToken);
        var now = clock.GetUtcNow();
        var requirement = Mutate(() =>
        {
            var created = new ProposalRequirement(Guid.NewGuid(), proposal.TenantId, id, command.ToDetails(), now);
            if (command.Status != ComplianceStatus.NotStarted)
            {
                created.Update(command.ToDetails(), command.Status, now);
            }

            return created;
        });
        await EnsureUniqueReferenceAsync(proposal, requirement.Reference, null, cancellationToken);
        db.ProposalRequirements.Add(requirement);
        Touch(proposal);
        await SaveAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.RequirementChanged, "proposal_requirement", requirement.Id.ToString(), AuditOutcome.Success, "created", cancellationToken);
        if (requirement.AssigneeUserId is not null)
        {
            await notifier.AssignedAsync(proposal, requirement.AssigneeUserId, $"requirement {requirement.Reference}", cancellationToken);
        }

        return await GetRequirementAsync(proposal, requirement.Id, cancellationToken);
    }

    public async Task<RequirementDto> UpdateRequirementAsync(Guid id, Guid requirementId, RequirementCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        tenant.RequireRole(CompanyRole.ProposalWriter);
        var proposal = await FindOpenAsync(id, cancellationToken);
        var requirement = await db.ProposalRequirements.SingleOrDefaultAsync(r => r.Id == requirementId && r.TenantId == proposal.TenantId && r.ProposalId == id, cancellationToken)
            ?? throw new ResourceNotFoundException();
        await ValidateRequirementLinksAsync(proposal, command, cancellationToken);
        var newAssignee = command.AssigneeUserId is not null && !string.Equals(command.AssigneeUserId, requirement.AssigneeUserId, StringComparison.Ordinal);
        Mutate(() => requirement.Update(command.ToDetails(), command.Status, clock.GetUtcNow()));
        await EnsureUniqueReferenceAsync(proposal, requirement.Reference, requirement.Id, cancellationToken);
        Touch(proposal);
        await SaveAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.RequirementChanged, "proposal_requirement", requirementId.ToString(), AuditOutcome.Success, $"status.{command.Status}", cancellationToken);
        if (newAssignee)
        {
            await notifier.AssignedAsync(proposal, command.AssigneeUserId!, $"requirement {requirement.Reference}", cancellationToken);
        }

        return await GetRequirementAsync(proposal, requirementId, cancellationToken);
    }

    public async Task DeleteRequirementAsync(Guid id, Guid requirementId, CancellationToken cancellationToken)
    {
        tenant.RequireRole(CompanyRole.ProposalWriter);
        var proposal = await FindOpenAsync(id, cancellationToken);
        var requirement = await db.ProposalRequirements.SingleOrDefaultAsync(r => r.Id == requirementId && r.TenantId == proposal.TenantId && r.ProposalId == id, cancellationToken)
            ?? throw new ResourceNotFoundException();
        db.ProposalRequirements.Remove(requirement);
        Touch(proposal);
        await SaveAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.RequirementChanged, "proposal_requirement", requirementId.ToString(), AuditOutcome.Success, "deleted", cancellationToken);
    }

    public async Task<ProposalDetailDto> RequestReviewAsync(Guid id, RequestReviewCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        tenant.RequireRole(CompanyRole.ProposalWriter);
        var userId = tenant.RequireUser();
        var proposal = await FindOpenAsync(id, cancellationToken);
        if (proposal.Status != ProposalStatus.InReview)
        {
            throw new ConflictException($"Move the proposal to “{Settings.LabelFor(ProposalStatus.InReview)}” before requesting a review.");
        }

        await db.EnsureActiveMemberAsync(proposal.TenantId, command.ReviewerUserId, "review.reviewer_not_member", cancellationToken);
        if (!Settings.AllowOwnerSelfReview && string.Equals(command.ReviewerUserId, proposal.OwnerUserId, StringComparison.Ordinal))
        {
            throw new RequestValidationException("review.self_review", "The proposal owner cannot review their own proposal. Choose another member.");
        }

        var duplicate = await db.ProposalReviews.AnyAsync(r => r.TenantId == proposal.TenantId && r.ProposalId == id && r.ReviewerUserId == command.ReviewerUserId && r.Outcome == ReviewOutcome.Requested, cancellationToken);
        if (duplicate)
        {
            throw new ConflictException("This person already has an open review request for this proposal.");
        }

        var review = Mutate(() => new ProposalReview(Guid.NewGuid(), proposal.TenantId, id, command.ReviewerUserId, userId, command.Instructions, clock.GetUtcNow()));
        db.ProposalReviews.Add(review);
        Touch(proposal);
        await SaveAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.ReviewRequested, "proposal_review", review.Id.ToString(), AuditOutcome.Success, "requested", cancellationToken);
        await notifier.ReviewRequestedAsync(proposal, review, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<ProposalDetailDto> DecideReviewAsync(Guid id, Guid reviewId, DecideReviewCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var userId = tenant.RequireUser();
        tenant.RequireRole(CompanyRole.Reviewer);
        var proposal = await FindOpenAsync(id, cancellationToken);
        var review = await db.ProposalReviews.SingleOrDefaultAsync(r => r.Id == reviewId && r.TenantId == proposal.TenantId && r.ProposalId == id, cancellationToken)
            ?? throw new ResourceNotFoundException();
        if (!string.Equals(review.ReviewerUserId, userId, StringComparison.Ordinal))
        {
            await audit.RecordAsync(AuditActions.ReviewDecided, "proposal_review", reviewId.ToString(), AuditOutcome.Denied, "review.not_assigned_reviewer", cancellationToken);
            throw new PolicyDeniedException("review.not_assigned_reviewer");
        }

        if (proposal.Status != ProposalStatus.InReview)
        {
            throw new ConflictException("The proposal is no longer in review.");
        }

        Mutate(() => review.Decide(command.Outcome, command.Comment, userId, clock.GetUtcNow()));
        Touch(proposal);
        await SaveAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.ReviewDecided, "proposal_review", reviewId.ToString(), AuditOutcome.Success, $"outcome.{command.Outcome}", cancellationToken);
        await notifier.ReviewDecidedAsync(proposal, review, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    internal static ComplianceSummaryDto Summarize(IEnumerable<(bool Mandatory, ComplianceStatus Status)> items)
    {
        var list = items.ToList();
        static bool Resolved(ComplianceStatus s) => s is ComplianceStatus.Compliant or ComplianceStatus.Exception;
        return new ComplianceSummaryDto(
            list.Count,
            list.Count(i => i.Mandatory),
            list.Count(i => i.Mandatory && !Resolved(i.Status)),
            list.Count(i => i.Status == ComplianceStatus.Compliant),
            list.Count(i => i.Status == ComplianceStatus.Exception),
            list.Count(i => i.Status == ComplianceStatus.NeedsReview));
    }

    private static (bool Approved, int Pending, int ChangesRequested) ApprovalState(IEnumerable<ProposalReview> reviews, DateTimeOffset? roundStart)
    {
        if (roundStart is null)
        {
            return (false, 0, 0);
        }

        var round = reviews.Where(r => r.RequestedAt >= roundStart && r.Outcome != ReviewOutcome.Superseded).ToList();
        var pending = round.Count(r => r.Outcome == ReviewOutcome.Requested);
        var changes = round.Count(r => r.Outcome == ReviewOutcome.ChangesRequested);
        return (round.Any(r => r.Outcome == ReviewOutcome.Approved) && pending == 0 && changes == 0, pending, changes);
    }

    private string? BlockedReason(Proposal proposal, ProposalStatus to, (bool Approved, int Pending, int ChangesRequested) approval, ComplianceSummaryDto compliance)
    {
        if (!ProposalLifecycle.CanTransition(proposal.Status, to))
        {
            return "proposal.transition_not_allowed";
        }

        if (ProposalOptions.IsFinalStage(to) && (tenant.Role is null || tenant.Role < Settings.MinimumRoleForFinalStages))
        {
            return "role.insufficient";
        }

        if (to is ProposalStatus.Approved && !approval.Approved)
        {
            return approval.ChangesRequested > 0 ? "proposal.review_changes_requested" : approval.Pending > 0 ? "proposal.review_pending" : "proposal.review_required";
        }

        if (to is ProposalStatus.ReadyForSubmission && Settings.RequireMandatoryRequirementsResolved && compliance.MandatoryOpen > 0)
        {
            return "proposal.mandatory_requirements_open";
        }

        return null;
    }

    private static string BlockedMessage(string reason) => reason switch
    {
        "proposal.transition_not_allowed" => "That status change is not allowed from the current status.",
        "proposal.review_changes_requested" => "A reviewer requested changes. Move the proposal back to work, address them and request review again.",
        "proposal.review_pending" => "Wait for every requested reviewer to answer.",
        "proposal.review_required" => "An assigned reviewer must approve the proposal first.",
        "proposal.mandatory_requirements_open" => "Every mandatory requirement must be Compliant or an explained Exception first.",
        _ => "The status change is blocked.",
    };

    private static void Mutate(Action action)
    {
        try
        {
            action();
        }
        catch (DomainException ex)
        {
            throw new RequestValidationException("proposal.invalid", ex.Message);
        }
    }

    private static T Mutate<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (DomainException ex)
        {
            throw new RequestValidationException("proposal.invalid", ex.Message);
        }
    }

    private void Touch(Proposal proposal) => proposal.Touch(clock.GetUtcNow());

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Someone else changed this proposal at the same time. Reload and try again.");
        }
    }

    private async Task<Proposal> FindAsync(Guid id, bool tracked, CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        var query = tracked ? db.Proposals : db.Proposals.AsNoTracking();
        var proposal = await query.SingleOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId, cancellationToken);
        if (proposal is null)
        {
            await audit.RecordAsync(AuditActions.AccessDenied, "proposal", id.ToString(), AuditOutcome.Denied, "not_found_or_cross_tenant", cancellationToken);
            throw new ResourceNotFoundException();
        }

        return proposal;
    }

    private async Task<Proposal> FindOpenAsync(Guid id, CancellationToken cancellationToken)
    {
        var proposal = await FindAsync(id, tracked: true, cancellationToken);
        if (proposal.IsClosed)
        {
            throw new ConflictException($"A proposal that is {Settings.LabelFor(proposal.Status)} cannot be changed.");
        }

        return proposal;
    }

    private async Task ValidateRequirementLinksAsync(Proposal proposal, RequirementCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Status))
        {
            throw new RequestValidationException("requirement.invalid", "Unknown compliance status.");
        }

        await db.EnsureActiveMemberAsync(proposal.TenantId, string.IsNullOrWhiteSpace(command.AssigneeUserId) ? null : command.AssigneeUserId, "requirement.assignee_not_member", cancellationToken);
        if (command.SectionId is { } sectionId && !await db.ProposalSections.AnyAsync(s => s.Id == sectionId && s.TenantId == proposal.TenantId && s.ProposalId == proposal.Id, cancellationToken))
        {
            throw new RequestValidationException("requirement.section_not_found", "The selected section does not belong to this proposal.");
        }
    }

    private async Task EnsureUniqueReferenceAsync(Proposal proposal, string reference, Guid? exceptId, CancellationToken cancellationToken)
    {
        var lowered = reference.ToLowerInvariant();
#pragma warning disable CA1862 // Translated to SQL lower(); string.Equals with a comparison is not translatable.
        var clash = await db.ProposalRequirements.AnyAsync(r => r.TenantId == proposal.TenantId && r.ProposalId == proposal.Id && r.Id != exceptId && r.Reference.ToLower() == lowered, cancellationToken);
#pragma warning restore CA1862
        if (clash)
        {
            throw new ConflictException($"Requirement reference “{reference}” is already used in this proposal.");
        }
    }

    private async Task<RequirementDto> GetRequirementAsync(Proposal proposal, Guid requirementId, CancellationToken cancellationToken) =>
        (await GetAsync(proposal.Id, cancellationToken)).Requirements.Single(r => r.Id == requirementId);
}

/// <summary>Orders references like "L.2" before "L.10" by comparing digit runs numerically.</summary>
public sealed class ReferenceComparer : IComparer<string>
{
    public static ReferenceComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return string.CompareOrdinal(x, y);
        }

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var si = i;
                var sj = j;
                while (i < x.Length && char.IsAsciiDigit(x[i]))
                {
                    i++;
                }

                while (j < y.Length && char.IsAsciiDigit(y[j]))
                {
                    j++;
                }

                var a = x[si..i].TrimStart('0');
                var b = y[sj..j].TrimStart('0');
                var byLength = a.Length.CompareTo(b.Length);
                if (byLength != 0)
                {
                    return byLength;
                }

                var byValue = string.CompareOrdinal(a, b);
                if (byValue != 0)
                {
                    return byValue;
                }
            }
            else
            {
                var c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (c != 0)
                {
                    return c;
                }

                i++;
                j++;
            }
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }
}
