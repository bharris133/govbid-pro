using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;

namespace GovBidPro.Application.Proposals;

public sealed record CreateProposalCommand(Guid OpportunityId, string? Title, ResponseType? ResponseType);

public sealed record UpdateProposalCommand(string Title, ResponseType ResponseType, string OwnerUserId);

public sealed record ChangeStatusCommand(ProposalStatus To, string? Note, string? SubmissionEvidence);

public sealed record SectionCommand(string Title, string? AssigneeUserId, bool IsComplete);

public sealed record RequirementCommand(
    string Reference,
    string Text,
    string? SourceLocator,
    string? Category,
    bool Mandatory,
    Guid? SectionId,
    string? ResponseLocation,
    string? AssigneeUserId,
    string? Evidence,
    ComplianceStatus Status = ComplianceStatus.NotStarted)
{
    public RequirementDetails ToDetails() => new(Reference, Text, SourceLocator, Category, Mandatory, SectionId, ResponseLocation, AssigneeUserId, Evidence);
}

public sealed record RequestReviewCommand(string ReviewerUserId, string? Instructions);

public sealed record DecideReviewCommand(ReviewOutcome Outcome, string? Comment);

public sealed record StageDto(ProposalStatus Status, string Label);

public sealed record NextStageDto(ProposalStatus Status, string Label, bool Allowed, string? BlockedReason);

public sealed record ComplianceSummaryDto(int Total, int Mandatory, int MandatoryOpen, int Compliant, int Exceptions, int NeedsReview);

public sealed record ProposalSummaryDto(
    Guid Id,
    string Title,
    Guid OpportunityId,
    string OpportunityTitle,
    ResponseType ResponseType,
    ProposalStatus Status,
    string StatusLabel,
    string Owner,
    DateTimeOffset? DueAt,
    ComplianceSummaryDto Compliance,
    DateTimeOffset UpdatedAt);

public sealed record SectionDto(Guid Id, int Order, string Title, string? AssigneeUserId, string? Assignee, bool IsComplete);

public sealed record RequirementDto(
    Guid Id,
    string Reference,
    string Text,
    string? SourceLocator,
    string? Category,
    bool Mandatory,
    Guid? SectionId,
    string? SectionTitle,
    string? ResponseLocation,
    string? AssigneeUserId,
    string? Assignee,
    string? Evidence,
    ComplianceStatus Status,
    DateTimeOffset UpdatedAt);

public sealed record ReviewDto(Guid Id, string ReviewerUserId, string Reviewer, string RequestedBy, string? Instructions, DateTimeOffset RequestedAt, ReviewOutcome Outcome, string? Comment, DateTimeOffset? DecidedAt, bool CurrentRound);

public sealed record ProposalEventDto(ProposalStatus From, ProposalStatus To, string FromLabel, string ToLabel, string Actor, string? Note, DateTimeOffset OccurredAt);

public sealed record ProposalDetailDto(
    Guid Id,
    string Title,
    ResponseType ResponseType,
    ProposalStatus Status,
    string StatusLabel,
    string OwnerUserId,
    string Owner,
    Guid OpportunityId,
    string OpportunityTitle,
    string? SolicitationNumber,
    bool OpportunityIsLive,
    string? OpportunitySourceUrl,
    string? SubmissionInstructions,
    DateTimeOffset? DueAt,
    string? DueText,
    string? SubmissionEvidence,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<StageDto> Stages,
    IReadOnlyList<NextStageDto> NextStages,
    ComplianceSummaryDto Compliance,
    IReadOnlyList<SectionDto> Sections,
    IReadOnlyList<RequirementDto> Requirements,
    IReadOnlyList<ReviewDto> Reviews,
    IReadOnlyList<ProposalEventDto> History);
