using GovBidPro.Application.Abstractions;
using GovBidPro.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GovBidPro.Application.Security;

/// <summary>Handoff §9B.3.</summary>
public sealed record DataAccessContext(Guid TenantId, string ActorId, ProcessingPurpose Purpose);

public sealed record PolicyDecision(bool Allowed, string ReasonCode)
{
    public static PolicyDecision Allow { get; } = new(true, "allowed");

    public static PolicyDecision Deny(string reasonCode) => new(false, reasonCode);
}

public interface IDataPolicyEvaluator
{
    /// <summary>Evaluates access to a tenant-owned document. Fails closed.</summary>
    Task<PolicyDecision> EvaluateAsync(DataAccessContext actor, Guid resourceId, CancellationToken ct);
}

/// <summary>
/// Classification x purpose matrix (§9B.1, §9C.1). Server-side and authoritative; any TypeScript
/// classification selector is a usability aid only.
/// </summary>
public static class ClassificationPolicy
{
    public static PolicyDecision Evaluate(DataClassification classification, ProcessingPurpose purpose, bool confidentialAiApproved)
    {
        return classification switch
        {
            DataClassification.ProhibitedClassified => PolicyDecision.Deny("classification.prohibited"),
            DataClassification.RestrictedPendingReview => PolicyDecision.Deny("classification.restricted_pending_review"),
            DataClassification.Public => PolicyDecision.Allow,
            DataClassification.BusinessInternal => purpose == ProcessingPurpose.AiAssist
                ? PolicyDecision.Deny("ai.classification_not_eligible")
                : PolicyDecision.Allow,
            DataClassification.ConfidentialCommercial => purpose == ProcessingPurpose.AiAssist
                ? (confidentialAiApproved ? PolicyDecision.Allow : PolicyDecision.Deny("ai.confidential_requires_ai02_approval"))
                : PolicyDecision.Allow,
            _ => PolicyDecision.Deny("classification.unknown"),
        };
    }
}

/// <summary>Data-handling switches that require owner approvals before they may be turned on.</summary>
public sealed class DataHandlingOptions
{
    public const string SectionName = "DataHandling";

    /// <summary>
    /// Gate AI-02: whether CONFIDENTIAL_COMMERCIAL content may ever be sent to an external AI
    /// provider. Must remain false until the owner/customer decision is recorded.
    /// </summary>
    public bool ConfidentialCommercialAiApproved { get; set; }
}

public sealed class DocumentDataPolicyEvaluator(IAppDbContext db, IOptions<DataHandlingOptions> options) : IDataPolicyEvaluator
{
    public async Task<PolicyDecision> EvaluateAsync(DataAccessContext actor, Guid resourceId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var document = await db.StoredDocuments.AsNoTracking()
            .Where(d => d.Id == resourceId && d.TenantId == actor.TenantId)
            .Select(d => new { d.Classification, d.State, d.ScanStatus })
            .SingleOrDefaultAsync(ct);

        if (document is null)
        {
            return PolicyDecision.Deny("resource.not_found_or_cross_tenant");
        }

        switch (document.State)
        {
            case DocumentState.Quarantined:
                return PolicyDecision.Deny("document.quarantined");
            case DocumentState.SoftDeleted:
                return PolicyDecision.Deny("document.deleted");
            case DocumentState.Purged:
                return PolicyDecision.Deny("document.purged");
        }

        var classification = ClassificationPolicy.Evaluate(document.Classification, actor.Purpose, options.Value.ConfidentialCommercialAiApproved);
        if (!classification.Allowed)
        {
            return classification;
        }

        if (actor.Purpose is ProcessingPurpose.Read or ProcessingPurpose.Export or ProcessingPurpose.AiAssist or ProcessingPurpose.Search
            && document.ScanStatus is not (ScanStatus.Clean or ScanStatus.DevelopmentBypass))
        {
            return PolicyDecision.Deny("document.scan_not_clean");
        }

        return PolicyDecision.Allow;
    }
}
