using GovBidPro.Domain.Common;
using GovBidPro.Domain.Enums;

namespace GovBidPro.Domain.Entities;

/// <summary>
/// A company's response to one opportunity (handoff §5, §7). Tenant-owned. Status changes go through
/// <see cref="ProposalLifecycle"/>; GovBid Pro never submits anything to an agency.
/// </summary>
public sealed class Proposal : ITenantOwned
{
    public const int MaxTitleLength = 300;
    public const int MaxEvidenceLength = 2000;

    private Proposal()
    {
        Title = string.Empty;
        OwnerUserId = string.Empty;
        CreatedByUserId = string.Empty;
    }

    public Proposal(Guid id, Guid tenantId, Guid opportunityId, int opportunityRevisionNumber, string title, ResponseType responseType, DateTimeOffset? dueAtSnapshot, string? dueTextSnapshot, string ownerUserId, DateTimeOffset createdAt)
    {
        if (tenantId == Guid.Empty || opportunityId == Guid.Empty)
        {
            throw new DomainException("Tenant and opportunity are required.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserId);
        Id = id;
        TenantId = tenantId;
        OpportunityId = opportunityId;
        OpportunityRevisionNumber = opportunityRevisionNumber;
        Title = NormalizeTitle(title);
        ResponseType = responseType;
        DueAtSnapshot = dueAtSnapshot;
        DueTextSnapshot = dueTextSnapshot;
        OwnerUserId = ownerUserId;
        CreatedByUserId = ownerUserId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Status = ProposalStatus.Draft;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid OpportunityId { get; private set; }

    /// <summary>The notice revision the proposal was started from; later amendments are flagged.</summary>
    public int OpportunityRevisionNumber { get; private set; }

    public string Title { get; private set; }

    /// <summary>Copied from the opportunity at creation and editable by the team (handoff §3.1).</summary>
    public ResponseType ResponseType { get; private set; }

    public ProposalStatus Status { get; private set; }

    /// <summary>The notice deadline when the proposal was created. The solicitation stays authoritative.</summary>
    public DateTimeOffset? DueAtSnapshot { get; private set; }

    public string? DueTextSnapshot { get; private set; }

    public string OwnerUserId { get; private set; }

    public string CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>User-entered evidence of an external submission (confirmation number, portal, time).</summary>
    public string? SubmissionEvidence { get; private set; }

    public bool IsClosed => Status is ProposalStatus.Won or ProposalStatus.Lost or ProposalStatus.Cancelled;

    public void Update(string title, ResponseType responseType, string ownerUserId, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserId);
        EnsureOpen();
        Title = NormalizeTitle(title);
        ResponseType = responseType;
        OwnerUserId = ownerUserId;
        UpdatedAt = at;
    }

    /// <summary>Applies a lifecycle transition after the caller has checked roles and review state.</summary>
    public ProposalEvent ChangeStatus(ProposalStatus to, bool hasRecordedReviewerApproval, string? submissionEvidence, string? note, string actorUserId, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        var evidence = string.IsNullOrWhiteSpace(submissionEvidence) ? null : submissionEvidence.Trim();
        if (evidence is { Length: > MaxEvidenceLength })
        {
            throw new DomainException($"Submission evidence must be at most {MaxEvidenceLength} characters.");
        }

        ProposalLifecycle.EnsureTransition(Status, to, hasRecordedReviewerApproval, evidence is not null);
        var from = Status;
        Status = to;
        UpdatedAt = at;
        if (to == ProposalStatus.SubmittedExternally)
        {
            SubmissionEvidence = evidence;
        }

        return new ProposalEvent(Guid.NewGuid(), TenantId, Id, from, to, note, actorUserId, at);
    }

    /// <summary>Records that something inside the proposal changed (also bumps its concurrency token).</summary>
    public void Touch(DateTimeOffset at) => UpdatedAt = at;

    private void EnsureOpen()
    {
        if (IsClosed)
        {
            throw new DomainException($"A proposal that is {Status} cannot be changed.");
        }
    }

    private static string NormalizeTitle(string? title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxTitleLength)
        {
            throw new DomainException($"Proposal title must be 1-{MaxTitleLength} characters.");
        }

        return trimmed;
    }
}

/// <summary>Immutable status-change record: the proposal's lifecycle audit trail.</summary>
public sealed class ProposalEvent : ITenantOwned
{
    private ProposalEvent()
    {
        ActorUserId = string.Empty;
    }

    public ProposalEvent(Guid id, Guid tenantId, Guid proposalId, ProposalStatus from, ProposalStatus to, string? note, string actorUserId, DateTimeOffset occurredAt)
    {
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is { Length: > 1000 })
        {
            throw new DomainException("Note must be at most 1000 characters.");
        }

        Id = id;
        TenantId = tenantId;
        ProposalId = proposalId;
        From = from;
        To = to;
        Note = trimmed;
        ActorUserId = actorUserId;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid ProposalId { get; private set; }

    public ProposalStatus From { get; private set; }

    public ProposalStatus To { get; private set; }

    public string? Note { get; private set; }

    public string ActorUserId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>A response section or volume with an assigned author (handoff §5 ProposalSection).</summary>
public sealed class ProposalSection : ITenantOwned
{
    private ProposalSection()
    {
        Title = string.Empty;
    }

    public ProposalSection(Guid id, Guid tenantId, Guid proposalId, int order, string title, string? assigneeUserId)
    {
        Id = id;
        TenantId = tenantId;
        ProposalId = proposalId;
        Order = order;
        Title = NormalizeTitle(title);
        AssigneeUserId = assigneeUserId;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid ProposalId { get; private set; }

    public int Order { get; private set; }

    public string Title { get; private set; }

    public string? AssigneeUserId { get; private set; }

    public bool IsComplete { get; private set; }

    public void Update(string title, string? assigneeUserId, bool isComplete)
    {
        Title = NormalizeTitle(title);
        AssigneeUserId = assigneeUserId;
        IsComplete = isComplete;
    }

    private static string NormalizeTitle(string? title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 200)
        {
            throw new DomainException("Section title must be 1-200 characters.");
        }

        return trimmed;
    }
}

/// <summary>
/// One solicitation requirement in the compliance matrix (handoff §7.2): where it comes from, what it
/// says, where the response addresses it, who owns it, the evidence and the compliance status. Status
/// is a team's own tracking; GovBid Pro makes no compliance determination.
/// </summary>
public sealed class ProposalRequirement : ITenantOwned
{
    public const int MaxTextLength = 4000;

    private ProposalRequirement()
    {
        Reference = string.Empty;
        Text = string.Empty;
    }

    public ProposalRequirement(Guid id, Guid tenantId, Guid proposalId, RequirementDetails details, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(details);
        Reference = string.Empty;
        Text = string.Empty;
        Id = id;
        TenantId = tenantId;
        ProposalId = proposalId;
        Status = ComplianceStatus.NotStarted;
        CreatedAt = at;
        Apply(details, at);
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid ProposalId { get; private set; }

    /// <summary>Unique within the proposal, e.g. "L.4.2" or "PWS 3.1".</summary>
    public string Reference { get; private set; }

    /// <summary>Where in the solicitation: document and page/paragraph.</summary>
    public string? SourceLocator { get; private set; }

    public string Text { get; private set; }

    public string? Category { get; private set; }

    public bool Mandatory { get; private set; }

    public Guid? SectionId { get; private set; }

    /// <summary>Where the response addresses it, e.g. "Vol I, 2.3, p. 7".</summary>
    public string? ResponseLocation { get; private set; }

    public string? AssigneeUserId { get; private set; }

    public string? Evidence { get; private set; }

    public ComplianceStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(RequirementDetails details, ComplianceStatus status, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (!Enum.IsDefined(status))
        {
            throw new DomainException("Unknown compliance status.");
        }

        if (status == ComplianceStatus.Exception && string.IsNullOrWhiteSpace(details.Evidence))
        {
            throw new DomainException("An exception needs an explanation in the evidence field.");
        }

        Apply(details, at);
        Status = status;
    }

    private void Apply(RequirementDetails d, DateTimeOffset at)
    {
        Reference = Required(d.Reference, 64, "Reference");
        Text = Required(d.Text, MaxTextLength, "Requirement text");
        SourceLocator = Optional(d.SourceLocator, 200, "Source locator");
        Category = Optional(d.Category, 64, "Category");
        ResponseLocation = Optional(d.ResponseLocation, 200, "Response location");
        Evidence = Optional(d.Evidence, 2000, "Evidence");
        Mandatory = d.Mandatory;
        SectionId = d.SectionId;
        AssigneeUserId = string.IsNullOrWhiteSpace(d.AssigneeUserId) ? null : d.AssigneeUserId;
        UpdatedAt = at;
    }

    private static string Required(string? value, int max, string name) =>
        Optional(value, max, name) ?? throw new DomainException($"{name} is required.");

    private static string? Optional(string? value, int max, string name)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (trimmed is { Length: var length } && length > max)
        {
            throw new DomainException($"{name} must be at most {max} characters.");
        }

        return trimmed;
    }
}

public sealed record RequirementDetails(
    string Reference,
    string Text,
    string? SourceLocator,
    string? Category,
    bool Mandatory,
    Guid? SectionId,
    string? ResponseLocation,
    string? AssigneeUserId,
    string? Evidence);

public enum ReviewOutcome
{
    Requested = 0,
    Approved = 1,
    ChangesRequested = 2,

    /// <summary>Withdrawn because the proposal went back to work before the reviewer answered.</summary>
    Superseded = 3,
}

/// <summary>A review request to a named reviewer and its outcome (handoff §7.2: explicit reviewer approval).</summary>
public sealed class ProposalReview : ITenantOwned
{
    private ProposalReview()
    {
        ReviewerUserId = string.Empty;
        RequestedByUserId = string.Empty;
    }

    public ProposalReview(Guid id, Guid tenantId, Guid proposalId, string reviewerUserId, string requestedByUserId, string? instructions, DateTimeOffset requestedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewerUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedByUserId);
        Id = id;
        TenantId = tenantId;
        ProposalId = proposalId;
        ReviewerUserId = reviewerUserId;
        RequestedByUserId = requestedByUserId;
        Instructions = Limit(instructions);
        RequestedAt = requestedAt;
        Outcome = ReviewOutcome.Requested;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid ProposalId { get; private set; }

    public string ReviewerUserId { get; private set; }

    public string RequestedByUserId { get; private set; }

    public string? Instructions { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public ReviewOutcome Outcome { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public void Decide(ReviewOutcome outcome, string? comment, string actorUserId, DateTimeOffset at)
    {
        if (outcome is not (ReviewOutcome.Approved or ReviewOutcome.ChangesRequested))
        {
            throw new DomainException("A review can only be approved or sent back with changes requested.");
        }

        if (!string.Equals(actorUserId, ReviewerUserId, StringComparison.Ordinal))
        {
            throw new DomainException("Only the assigned reviewer can record this review.");
        }

        if (Outcome != ReviewOutcome.Requested)
        {
            throw new DomainException("This review has already been answered.");
        }

        var trimmed = Limit(comment);
        if (outcome == ReviewOutcome.ChangesRequested && trimmed is null)
        {
            throw new DomainException("Say what needs to change.");
        }

        Outcome = outcome;
        Comment = trimmed;
        DecidedAt = at;
    }

    public void Supersede(DateTimeOffset at)
    {
        if (Outcome == ReviewOutcome.Requested)
        {
            Outcome = ReviewOutcome.Superseded;
            DecidedAt = at;
        }
    }

    private static string? Limit(string? value)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return trimmed is { Length: > 2000 } ? throw new DomainException("Text must be at most 2000 characters.") : trimmed;
    }
}
