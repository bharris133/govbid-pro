using GovBidPro.Domain.Common;

namespace GovBidPro.Domain.Entities;

/// <summary>Proposed proposal lifecycle (handoff §7.3). Terminology to be confirmed with stakeholders.</summary>
public enum ProposalStatus
{
    Draft = 0,
    InProgress = 1,
    InReview = 2,
    Approved = 3,
    ReadyForSubmission = 4,

    /// <summary>User-recorded evidence that the user submitted externally. GovBid Pro never submits to agencies.</summary>
    SubmittedExternally = 5,
    Won = 6,
    Lost = 7,
    Cancelled = 8,
}

/// <summary>
/// Allowed proposal status transitions. P0 ships the state machine with tests; the proposal
/// aggregate, persistence and API arrive in P1.4.
/// </summary>
public static class ProposalLifecycle
{
    private static readonly Dictionary<ProposalStatus, ProposalStatus[]> Allowed = new()
    {
        [ProposalStatus.Draft] = [ProposalStatus.InProgress, ProposalStatus.Cancelled],
        [ProposalStatus.InProgress] = [ProposalStatus.InReview, ProposalStatus.Cancelled],
        [ProposalStatus.InReview] = [ProposalStatus.Approved, ProposalStatus.InProgress, ProposalStatus.Cancelled],
        [ProposalStatus.Approved] = [ProposalStatus.ReadyForSubmission, ProposalStatus.InProgress, ProposalStatus.Cancelled],
        [ProposalStatus.ReadyForSubmission] = [ProposalStatus.SubmittedExternally, ProposalStatus.InProgress, ProposalStatus.Cancelled],
        [ProposalStatus.SubmittedExternally] = [ProposalStatus.Won, ProposalStatus.Lost, ProposalStatus.Cancelled],
        [ProposalStatus.Won] = [],
        [ProposalStatus.Lost] = [],
        [ProposalStatus.Cancelled] = [],
    };

    public static bool CanTransition(ProposalStatus from, ProposalStatus to) =>
        Allowed.TryGetValue(from, out var targets) && Array.IndexOf(targets, to) >= 0;

    /// <summary>
    /// Validates a transition. Moving to Approved or ReadyForSubmission requires an explicit
    /// reviewer approval (§7.2). Moving to SubmittedExternally requires user-entered evidence.
    /// </summary>
    public static void EnsureTransition(ProposalStatus from, ProposalStatus to, bool hasRecordedReviewerApproval, bool hasSubmissionEvidence)
    {
        if (!CanTransition(from, to))
        {
            throw new DomainException($"Transition {from} -> {to} is not allowed.");
        }

        if (to is ProposalStatus.Approved or ProposalStatus.ReadyForSubmission && !hasRecordedReviewerApproval)
        {
            throw new DomainException($"Transition to {to} requires a recorded reviewer approval.");
        }

        if (to == ProposalStatus.SubmittedExternally && !hasSubmissionEvidence)
        {
            throw new DomainException("Recording external submission requires user-entered evidence.");
        }
    }
}
