namespace GovBidPro.Domain.Enums;

/// <summary>
/// Product routing classification (handoff §9B.1). These are not legal determinations:
/// automated markers are detection signals only and cannot prove the absence of FCI/CUI.
/// </summary>
public enum DataClassification
{
    /// <summary>Published solicitations and other public content.</summary>
    Public = 0,

    /// <summary>Company profiles, contact details, internal tasks. Tenant-scoped.</summary>
    BusinessInternal = 1,

    /// <summary>Nonpublic proposal drafts, pricing, past performance, uploads. Tenant-scoped, private by default.</summary>
    ConfidentialCommercial = 2,

    /// <summary>User-declared or suspected FCI/CUI or security markings. Release 1: reject before storage.</summary>
    RestrictedPendingReview = 3,

    /// <summary>Classified information. Must never be accepted.</summary>
    ProhibitedClassified = 4,
}
