using GovBidPro.Domain.Enums;

namespace GovBidPro.Domain.Entities;

/// <summary>
/// Content-free AI request metadata (§9C.1; 90-day provisional retention under D-02).
/// Never stores prompts, responses or confidential text.
/// </summary>
public sealed class AiRequestRecord
{
    private AiRequestRecord()
    {
        Feature = string.Empty;
        ReasonCode = string.Empty;
        ActorUserId = string.Empty;
        CorrelationId = string.Empty;
    }

    public AiRequestRecord(Guid id, Guid tenantId, string actorUserId, string feature, ProcessingPurpose purpose, AiDecision decision, string reasonCode, string? provider, string? model, string? promptTemplateVersion, IReadOnlyList<Guid> inputResourceIds, DataClassification? highestInputClassification, string correlationId, DateTimeOffset occurredAt)
    {
        Id = id;
        TenantId = tenantId;
        ActorUserId = actorUserId;
        Feature = feature;
        Purpose = purpose;
        Decision = decision;
        ReasonCode = reasonCode;
        Provider = provider;
        Model = model;
        PromptTemplateVersion = promptTemplateVersion;
        InputResourceIds = [.. inputResourceIds];
        HighestInputClassification = highestInputClassification;
        CorrelationId = correlationId;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string ActorUserId { get; private set; }

    public string Feature { get; private set; }

    public ProcessingPurpose Purpose { get; private set; }

    public AiDecision Decision { get; private set; }

    public string ReasonCode { get; private set; }

    public string? Provider { get; private set; }

    public string? Model { get; private set; }

    public string? PromptTemplateVersion { get; private set; }

    public List<Guid> InputResourceIds { get; private set; } = [];

    public DataClassification? HighestInputClassification { get; private set; }

    public int? InputTokens { get; private set; }

    public int? OutputTokens { get; private set; }

    public string? ResponseStatus { get; private set; }

    public string CorrelationId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public void RecordProviderResult(string responseStatus, int? inputTokens, int? outputTokens)
    {
        ResponseStatus = responseStatus;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
    }
}
