namespace GovBidPro.Domain.Enums;

/// <summary>
/// Company-scoped roles from requirements.md §1. Platform administration is a separate
/// Identity role and is never granted through company membership (handoff §9D).
/// </summary>
public enum CompanyRole
{
    Reviewer = 0,
    ProposalWriter = 1,
    TeamManager = 2,
    CompanyAdmin = 3,
}
