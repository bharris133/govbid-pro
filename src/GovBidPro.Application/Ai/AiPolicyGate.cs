using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Security;
using GovBidPro.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GovBidPro.Application.Ai;

public enum AiInputKind
{
    PublicOpportunity = 0,
    TenantDocument = 1,
}

public sealed record AiInput(AiInputKind Kind, Guid ResourceId);

public sealed record AiGateDecision(bool Allowed, string ReasonCode, DataClassification? HighestClassification);

public interface IAiPolicyGate
{
    Task<AiGateDecision> EvaluateAsync(string feature, IReadOnlyList<AiInput> inputs, CancellationToken cancellationToken);
}

/// <summary>
/// Fail-closed AI policy gate (§9C.2): platform switch → provider approval → feature allowlist →
/// tenant consent → input ownership → classification eligibility. Runs before any retrieval or provider call.
/// </summary>
public sealed class AiPolicyGate(IAppDbContext db, ITenantContext tenant, IOptions<AiOptions> aiOptions, IOptions<DataHandlingOptions> dataOptions) : IAiPolicyGate
{
    public async Task<AiGateDecision> EvaluateAsync(string feature, IReadOnlyList<AiInput> inputs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var options = aiOptions.Value;
        if (!tenant.HasTenant || tenant.UserId is null)
        {
            return Deny("tenant.not_resolved");
        }

        if (!options.PlatformEnabled)
        {
            return Deny("ai.platform_disabled");
        }

        if (string.Equals(options.Provider, "None", StringComparison.OrdinalIgnoreCase)
            || !options.AllowedProviders.Contains(options.Provider, StringComparer.OrdinalIgnoreCase))
        {
            return Deny("ai.provider_not_allowlisted");
        }

        if (!string.Equals(options.Provider, "Fake", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(options.Ai01ApprovalReference))
        {
            return Deny("ai.ai01_approval_missing");
        }

        if (!options.EnabledFeatures.Contains(feature, StringComparer.Ordinal))
        {
            return Deny("ai.feature_not_enabled");
        }

        var tenantId = tenant.TenantId;
        var consent = await db.Companies.AsNoTracking().Where(c => c.Id == tenantId).Select(c => new { c.AiEnabled, c.Status }).SingleOrDefaultAsync(cancellationToken);
        if (consent is null || consent.Status != TenantStatus.Active || !consent.AiEnabled)
        {
            return Deny("ai.tenant_consent_missing");
        }

        if (inputs.Count == 0)
        {
            return Deny("ai.no_inputs");
        }

        DataClassification highest = DataClassification.Public;
        foreach (var input in inputs)
        {
            DataClassification classification;
            switch (input.Kind)
            {
                case AiInputKind.PublicOpportunity:
                    var exists = await db.Opportunities.AsNoTracking().AnyAsync(o => o.Id == input.ResourceId, cancellationToken);
                    if (!exists)
                    {
                        return Deny("ai.input_not_found");
                    }

                    classification = DataClassification.Public;
                    break;
                case AiInputKind.TenantDocument:
                    var document = await db.StoredDocuments.AsNoTracking()
                        .Where(d => d.Id == input.ResourceId && d.TenantId == tenantId)
                        .Select(d => new { d.Classification, d.State, d.ScanStatus })
                        .SingleOrDefaultAsync(cancellationToken);
                    if (document is null)
                    {
                        // AI-02 test case: cross-tenant identifiers are rejected before retrieval or provider call.
                        return Deny("ai.input_not_found_or_cross_tenant");
                    }

                    if (document.State != DocumentState.Active || document.ScanStatus is not (ScanStatus.Clean or ScanStatus.DevelopmentBypass))
                    {
                        return Deny("ai.input_not_available");
                    }

                    classification = document.Classification;
                    break;
                default:
                    return Deny("ai.input_kind_unknown");
            }

            var decision = ClassificationPolicy.Evaluate(classification, ProcessingPurpose.AiAssist, dataOptions.Value.ConfidentialCommercialAiApproved);
            if (!decision.Allowed)
            {
                return new AiGateDecision(false, decision.ReasonCode, classification);
            }

            highest = classification > highest ? classification : highest;
        }

        return new AiGateDecision(true, "allowed", highest);
    }

    private static AiGateDecision Deny(string reason) => new(false, reason, null);
}
