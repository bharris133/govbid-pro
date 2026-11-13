namespace GovBidPro.Infrastructure.SamGov;

/// <summary>
/// Placeholder for the official SAM.gov adapter (milestone P1.2). Intentionally not implemented and
/// not registered: endpoints, API-key requirements, quotas, pagination, field semantics and permitted
/// use must be verified against current official SAM.gov documentation before any live call is written.
/// Until then GovBid Pro runs in fixture-only mode, which is labeled as demo data in the UI.
/// </summary>
public static class SamGovIntegrationStatus
{
    public const string SourceKey = "sam.gov";

    public const bool LiveIntegrationVerified = false;

    public const string Note = "Official SAM.gov API access not yet verified (P1.2). Fixture-only mode.";
}
