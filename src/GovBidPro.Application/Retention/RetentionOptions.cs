namespace GovBidPro.Application.Retention;

/// <summary>
/// Retention configuration (RET-02: configure periods, do not hardcode them). Values for Release 1
/// are the provisional owner defaults under D-02 and remain subject to legal review (LEG-01).
/// Every key in <see cref="RetentionPolicyKeys.Required"/> must be configured or startup fails.
/// </summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>Recorded on every policy version, e.g. "provisional (D-02); legal review pending (LEG-01)".</summary>
    public string ApprovalStatus { get; set; } = string.Empty;

    public Dictionary<string, RetentionPeriodOptions> Policies { get; set; } = new(StringComparer.Ordinal);

    public RetentionJobOptions PurgeJob { get; set; } = new();
}

public sealed class RetentionPeriodOptions
{
    /// <summary>Retention period in days; null means "while the account is active" (no fixed period).</summary>
    public int? Days { get; set; }

    /// <summary>Must be set explicitly to true for policies without a fixed period, to avoid accidental nulls.</summary>
    public bool RetainWhileAccountActive { get; set; }
}

public sealed class RetentionJobOptions
{
    /// <summary>Automated purge. Defaults to disabled outside Development until LEG-01 sign-off.</summary>
    public bool Enabled { get; set; }

    public int IntervalMinutes { get; set; } = 60;

    public int BatchSize { get; set; } = 100;
}

public static class RetentionPolicyKeys
{
    public const string ActiveDocuments = "active-documents";
    public const string DeletedDocumentRecovery = "deleted-document-recovery";
    public const string ClosedAccountExport = "closed-account-export";
    public const string DatabaseBackups = "database-backups";
    public const string SecurityAuditEvents = "security-audit-events";
    public const string AiRequestMetadata = "ai-request-metadata";

    public static IReadOnlyList<string> Required { get; } =
    [
        ActiveDocuments,
        DeletedDocumentRecovery,
        ClosedAccountExport,
        DatabaseBackups,
        SecurityAuditEvents,
        AiRequestMetadata,
    ];

    /// <summary>Validates configuration. Returns error messages; empty when valid.</summary>
    public static IReadOnlyList<string> Validate(RetentionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ApprovalStatus))
        {
            errors.Add("Retention:ApprovalStatus must be configured.");
        }

        foreach (var key in Required)
        {
            if (!options.Policies.TryGetValue(key, out var period))
            {
                errors.Add($"Retention:Policies:{key} must be configured.");
                continue;
            }

            if (period.Days is null && !period.RetainWhileAccountActive)
            {
                errors.Add($"Retention:Policies:{key} needs Days or RetainWhileAccountActive=true.");
            }

            if (period.Days is <= 0)
            {
                errors.Add($"Retention:Policies:{key}:Days must be positive.");
            }
        }

        return errors;
    }
}
