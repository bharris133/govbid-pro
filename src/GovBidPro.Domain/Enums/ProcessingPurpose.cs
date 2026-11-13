namespace GovBidPro.Domain.Enums;

/// <summary>Purpose limitation used by the data-policy evaluator (handoff §9B.3).</summary>
public enum ProcessingPurpose
{
    Store = 0,
    Read = 1,
    Search = 2,
    Export = 3,
    Notify = 4,
    AiAssist = 5,
}
