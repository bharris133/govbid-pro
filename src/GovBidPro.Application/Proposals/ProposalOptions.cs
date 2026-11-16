using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;

namespace GovBidPro.Application.Proposals;

/// <summary>
/// Proposal workflow settings. Stage display names and default sections are configuration so the
/// product owner can rename stages without code changes (open question 3); the states and allowed
/// transitions are fixed in <see cref="ProposalLifecycle"/>.
/// </summary>
public sealed class ProposalOptions
{
    public const string SectionName = "Proposals";

    /// <summary>Display name per status. Missing entries fall back to <see cref="DefaultStageLabels"/>.</summary>
    public Dictionary<string, string> StageLabels { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Lowest role that may move a proposal to Approved, ReadyForSubmission, SubmittedExternally, Won, Lost or Cancelled.</summary>
    public CompanyRole MinimumRoleForFinalStages { get; set; } = CompanyRole.TeamManager;

    /// <summary>Whether the proposal owner may be its own reviewer. Off by default (four-eyes review).</summary>
    public bool AllowOwnerSelfReview { get; set; }

    /// <summary>ReadyForSubmission requires every mandatory requirement to be Compliant or an explained Exception.</summary>
    public bool RequireMandatoryRequirementsResolved { get; set; } = true;

    /// <summary>Default sections per response type. Missing entries fall back to <see cref="DefaultSections"/>.</summary>
    public Dictionary<string, List<string>> SectionTemplates { get; set; } = new(StringComparer.Ordinal);

    public int MaxRequirementsPerProposal { get; set; } = 2000;

    public static IReadOnlyDictionary<ProposalStatus, string> DefaultStageLabels { get; } = new Dictionary<ProposalStatus, string>
    {
        [ProposalStatus.Draft] = "Draft",
        [ProposalStatus.InProgress] = "In progress",
        [ProposalStatus.InReview] = "In review",
        [ProposalStatus.Approved] = "Approved",
        [ProposalStatus.ReadyForSubmission] = "Ready for submission",
        [ProposalStatus.SubmittedExternally] = "Submitted (outside GovBid Pro)",
        [ProposalStatus.Won] = "Won",
        [ProposalStatus.Lost] = "Lost",
        [ProposalStatus.Cancelled] = "Cancelled",
    };

    public static IReadOnlyDictionary<ResponseType, string[]> DefaultSections { get; } = new Dictionary<ResponseType, string[]>
    {
        [ResponseType.Rfp] = ["Cover letter", "Technical approach", "Management approach", "Past performance", "Price"],
        [ResponseType.Rfq] = ["Quote and line items", "Delivery terms", "Technical compliance", "Representations and certifications"],
        [ResponseType.Rfi] = ["Company overview", "Responses to questions", "Recommendations to the government"],
        [ResponseType.SourcesSought] = ["Capability statement", "Relevant experience", "Business size and set-aside status"],
        [ResponseType.Undetermined] = ["Response"],
        [ResponseType.NoResponseExpected] = ["Notes"],
    };

    public string LabelFor(ProposalStatus status) =>
        StageLabels.TryGetValue(status.ToString(), out var label) && !string.IsNullOrWhiteSpace(label) ? label : DefaultStageLabels[status];

    public IReadOnlyList<string> SectionsFor(ResponseType type) =>
        SectionTemplates.TryGetValue(type.ToString(), out var configured) && configured.Count > 0 ? configured : DefaultSections[type];

    public static bool IsValid(ProposalOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return o.StageLabels.All(kv => Enum.TryParse<ProposalStatus>(kv.Key, out _) && kv.Value.Length is > 0 and <= 60)
            && o.SectionTemplates.All(kv => Enum.TryParse<ResponseType>(kv.Key, out _) && kv.Value.Count <= 30 && kv.Value.All(v => v.Length is > 0 and <= 200))
            && o.MaxRequirementsPerProposal is > 0 and <= 10000;
    }

    /// <summary>Statuses whose entry needs <see cref="MinimumRoleForFinalStages"/>.</summary>
    public static bool IsFinalStage(ProposalStatus status) =>
        status is ProposalStatus.Approved or ProposalStatus.ReadyForSubmission or ProposalStatus.SubmittedExternally or ProposalStatus.Won or ProposalStatus.Lost or ProposalStatus.Cancelled;
}
