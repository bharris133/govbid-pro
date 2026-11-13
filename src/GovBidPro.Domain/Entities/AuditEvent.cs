using GovBidPro.Domain.Enums;

namespace GovBidPro.Domain.Entities;

/// <summary>
/// Append-only security audit record (handoff §5, §9). Must never contain document bodies,
/// prompts, credentials or secrets: only identifiers, reason codes and bounded metadata.
/// </summary>
public sealed class AuditEvent
{
    private AuditEvent()
    {
        Action = string.Empty;
        ResourceType = string.Empty;
        CorrelationId = string.Empty;
        ReasonCode = string.Empty;
    }

    public AuditEvent(Guid id, Guid? tenantId, string? actorUserId, string action, string resourceType, string? resourceId, AuditOutcome outcome, string reasonCode, string correlationId, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        Id = id;
        TenantId = tenantId;
        ActorUserId = actorUserId;
        Action = action;
        ResourceType = resourceType;
        ResourceId = resourceId;
        Outcome = outcome;
        ReasonCode = reasonCode;
        CorrelationId = correlationId;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    public Guid? TenantId { get; private set; }

    public string? ActorUserId { get; private set; }

    public string Action { get; private set; }

    public string ResourceType { get; private set; }

    public string? ResourceId { get; private set; }

    public AuditOutcome Outcome { get; private set; }

    public string ReasonCode { get; private set; }

    public string CorrelationId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}
