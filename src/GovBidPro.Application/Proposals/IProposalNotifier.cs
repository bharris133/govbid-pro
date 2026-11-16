using GovBidPro.Domain.Entities;

namespace GovBidPro.Application.Proposals;

/// <summary>
/// Collaboration hook: tells people about assignments and reviews. The P1.4 default does nothing;
/// P1.6 replaces it with in-app notifications and email through the provider-neutral email port.
/// Implementations must never throw for delivery problems; the proposal change has already been saved.
/// </summary>
public interface IProposalNotifier
{
    Task AssignedAsync(Proposal proposal, string assigneeUserId, string what, CancellationToken cancellationToken);

    Task ReviewRequestedAsync(Proposal proposal, ProposalReview review, CancellationToken cancellationToken);

    Task ReviewDecidedAsync(Proposal proposal, ProposalReview review, CancellationToken cancellationToken);
}

public sealed class NoOpProposalNotifier : IProposalNotifier
{
    public Task AssignedAsync(Proposal proposal, string assigneeUserId, string what, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReviewRequestedAsync(Proposal proposal, ProposalReview review, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReviewDecidedAsync(Proposal proposal, ProposalReview review, CancellationToken cancellationToken) => Task.CompletedTask;
}
