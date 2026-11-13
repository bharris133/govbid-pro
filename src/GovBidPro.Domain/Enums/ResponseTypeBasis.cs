namespace GovBidPro.Domain.Enums;

/// <summary>Records why an opportunity has its current interpreted response type.</summary>
public enum ResponseTypeBasis
{
    /// <summary>Derived by the versioned mapping rules in the application layer.</summary>
    MappingRule = 0,

    /// <summary>Set explicitly by an authorized user (overrides mapping rules).</summary>
    UserOverride = 1,
}
