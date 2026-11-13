namespace GovBidPro.Domain.Enums;

/// <summary>
/// The notice type as published by the originating source, normalized to an internal vocabulary.
/// This is a separate dimension from <see cref="ResponseType"/> (handoff §3.1, §5): there is no
/// one-to-one mapping. The verbatim source value is always retained alongside this value.
/// Mapping from official SAM.gov codes must be verified against official documentation (P1.2).
/// </summary>
public enum SourceNoticeType
{
    Unknown = 0,
    PreSolicitation = 1,
    Solicitation = 2,
    CombinedSynopsisSolicitation = 3,
    SourcesSought = 4,
    SpecialNotice = 5,
    AwardNotice = 6,
    SoleSource = 7,
    Other = 99,
}
