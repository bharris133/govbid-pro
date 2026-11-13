using GovBidPro.Application.Ai;
using Microsoft.Extensions.Options;

namespace GovBidPro.Infrastructure.Ai;

/// <summary>Default provider when AI is off. The policy gate denies first; this is a second fail-closed layer.</summary>
internal sealed class DisabledAiProvider : IAiProvider
{
    public string Name => "None";

    public string? Model => null;

    public Task<AiProviderResult> GenerateAsync(AiProviderRequest request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("AI is disabled; no provider call is permitted.");
}

/// <summary>
/// Azure-hosted AI adapter (D-03, AI-AZ-01). Deliberately not implemented in P0: the specific Azure AI
/// service, model, deployment, region, terms and retention settings require gate AI-01 approval first.
/// When approved, implement here with managed identity and keep SDK types inside this class.
/// </summary>
internal sealed class AzureAiProvider(IOptions<AiOptions> options) : IAiProvider
{
    public string Name => "Azure";

    public string? Model => options.Value.Azure.Deployment;

    public Task<AiProviderResult> GenerateAsync(AiProviderRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Azure AI provider is pending gate AI-01 (service/model/deployment/region/terms approval).");
}
