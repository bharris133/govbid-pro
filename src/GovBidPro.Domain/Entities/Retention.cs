using GovBidPro.Domain.Common;
using GovBidPro.Domain.Enums;

namespace GovBidPro.Domain.Entities;

/// <summary>
/// A versioned retention policy (RET-01). Values come from configuration (RET-02) and are
/// provisional under owner decision D-02 until legal sign-off (LEG-01). Never hardcode periods.
/// </summary>
public sealed class RetentionPolicy
{
    private RetentionPolicy()
    {
        PolicyKey = string.Empty;
        ApprovalStatus = string.Empty;
    }

    public RetentionPolicy(Guid id, string policyKey, int version, int? retentionDays, string approvalStatus, DateTimeOffset effectiveFrom)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyKey);
        if (retentionDays is < 0)
        {
            throw new DomainException("Retention days cannot be negative.");
        }

        Id = id;
        PolicyKey = policyKey;
        Version = version;
        RetentionDays = retentionDays;
        ApprovalStatus = approvalStatus;
        EffectiveFrom = effectiveFrom;
    }

    public Guid Id { get; private set; }

    public string PolicyKey { get; private set; }

    public int Version { get; private set; }

    /// <summary>Null means "retain while the account is active" (no fixed period).</summary>
    public int? RetentionDays { get; private set; }

    /// <summary>For example "provisional (D-02); legal review pending (LEG-01)".</summary>
    public string ApprovalStatus { get; private set; }

    public DateTimeOffset EffectiveFrom { get; private set; }

    public DateTimeOffset? SupersededAt { get; private set; }

    public void Supersede(DateTimeOffset at) => SupersededAt = at;
}

/// <summary>Audited record of a retention-relevant action (RET-01). Dates are UTC.</summary>
public sealed class RetentionAction : ITenantOwned
{
    private RetentionAction()
    {
        ResourceType = string.Empty;
        CorrelationId = string.Empty;
    }

    public RetentionAction(
        Guid id,
        Guid tenantId,
        string resourceType,
        Guid resourceId,
        DataClassification classification,
        Guid policyId,
        int policyVersion,
        RetentionActionType action,
        RetentionActionStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset? recoverableUntil,
        bool legalHold,
        string? actorUserId,
        string correlationId)
    {
        Id = id;
        TenantId = tenantId;
        ResourceType = resourceType;
        ResourceId = resourceId;
        Classification = classification;
        PolicyId = policyId;
        PolicyVersion = policyVersion;
        Action = action;
        Status = status;
        CreatedAt = createdAt;
        RecoverableUntil = recoverableUntil;
        LegalHold = legalHold;
        ActorUserId = actorUserId;
        CorrelationId = correlationId;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string ResourceType { get; private set; }

    public Guid ResourceId { get; private set; }

    public DataClassification Classification { get; private set; }

    public Guid PolicyId { get; private set; }

    public int PolicyVersion { get; private set; }

    public RetentionActionType Action { get; private set; }

    public RetentionActionStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RecoverableUntil { get; private set; }

    public bool LegalHold { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? FailureReason { get; private set; }

    public string? ActorUserId { get; private set; }

    public string CorrelationId { get; private set; }

    public void MarkCompleted(DateTimeOffset at)
    {
        Status = RetentionActionStatus.Completed;
        CompletedAt = at;
        FailureReason = null;
    }

    public void MarkFailed(string reason)
    {
        Status = RetentionActionStatus.Failed;
        FailureReason = reason;
    }

    public void Cancel(DateTimeOffset at)
    {
        Status = RetentionActionStatus.Cancelled;
        CompletedAt = at;
    }
}
