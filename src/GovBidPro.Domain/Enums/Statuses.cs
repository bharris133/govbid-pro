namespace GovBidPro.Domain.Enums;

public enum TenantStatus
{
    Active = 0,
    Suspended = 1,
    ClosurePending = 2,
    Closed = 3,
}

public enum MembershipStatus
{
    Invited = 0,
    Active = 1,
    Suspended = 2,
    Removed = 3,
}

public enum ScanStatus
{
    /// <summary>Scan has not completed. Downloads are denied.</summary>
    Pending = 0,

    /// <summary>Scanner reported the object clean.</summary>
    Clean = 1,

    /// <summary>Scanner reported malware. Object is quarantined.</summary>
    Infected = 2,

    /// <summary>No scanner is configured. Downloads are denied (deny by default).</summary>
    ScannerNotConfigured = 3,

    /// <summary>Local development only: scanning bypassed by explicit configuration.</summary>
    DevelopmentBypass = 4,
}

public enum DocumentState
{
    Active = 0,
    Quarantined = 1,
    SoftDeleted = 2,
    Purged = 3,
}

public enum AuditOutcome
{
    Success = 0,
    Denied = 1,
    Failed = 2,
}

public enum ImportRunStatus
{
    Running = 0,
    Succeeded = 1,
    PartiallySucceeded = 2,
    Failed = 3,
    RateLimited = 4,
}

public enum RevisionChangeKind
{
    Created = 0,
    Amended = 1,
}

public enum RetentionActionType
{
    SoftDelete = 0,
    Restore = 1,
    Purge = 2,
    LegalHoldApplied = 3,
    LegalHoldReleased = 4,
}

public enum RetentionActionStatus
{
    Scheduled = 0,
    Completed = 1,
    Failed = 2,
    SuspendedByLegalHold = 3,
    Cancelled = 4,
}

public enum NotificationFrequency
{
    None = 0,
    Daily = 1,
    Weekly = 2,
}

public enum ComplianceStatus
{
    NotStarted = 0,
    InProgress = 1,
    NeedsReview = 2,
    Compliant = 3,
    Exception = 4,
}

public enum AiDecision
{
    Allowed = 0,
    Denied = 1,
}
