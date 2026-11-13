namespace GovBidPro.Application.Ai;

/// <summary>
/// Provider-neutral AI port (AI-AZ-01). Implementations live in Infrastructure; no provider SDK types
/// may appear in Domain, Application contracts or controller DTOs.
/// </summary>
public interface IAiProvider
{
    string Name { get; }

    string? Model { get; }

    Task<AiProviderResult> GenerateAsync(AiProviderRequest request, CancellationToken cancellationToken);
}

/// <param name="SystemInstructions">Fixed, application-owned instructions.</param>
/// <param name="UntrustedSourceText">Source material. Always data, never instructions (§9C.1 prompt injection).</param>
public sealed record AiProviderRequest(string Feature, string PromptTemplateVersion, string SystemInstructions, string UntrustedSourceText, int MaxOutputTokens);

public sealed record AiProviderResult(string Text, int? InputTokens, int? OutputTokens);

/// <summary>Provider unavailable, throttled or timed out. Message must not contain source text.</summary>
public sealed class AiProviderUnavailableException : Exception
{
    public AiProviderUnavailableException()
    {
    }

    public AiProviderUnavailableException(string message)
        : base(message)
    {
    }

    public AiProviderUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public bool IsRateLimited { get; init; }
}

/// <summary>AI configuration. Everything defaults to OFF (D-03, AI-AZ-05).</summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Platform-wide kill switch. False unless gate AI-01 is approved for a specific pilot.</summary>
    public bool PlatformEnabled { get; set; }

    /// <summary>"None" (default) or "Azure". "Fake" is for automated tests only.</summary>
    public string Provider { get; set; } = "None";

    /// <summary>Reference to the recorded AI-01 approval (service/model/deployment/region/terms). Required for any real provider.</summary>
    public string? Ai01ApprovalReference { get; set; }

    /// <summary>Per-feature allowlist. Empty by default.</summary>
    public List<string> EnabledFeatures { get; set; } = [];

    public int MaxInputCharacters { get; set; } = 20_000;

    public int MaxOutputTokens { get; set; } = 800;

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Provider allowlist for egress. A provider not listed here is never called.</summary>
    public List<string> AllowedProviders { get; set; } = [];

    public AzureAiOptions Azure { get; set; } = new();
}

/// <summary>Azure AI settings (AI-AZ-02). Values come from configuration/Key Vault; credentials via managed identity.</summary>
public sealed class AzureAiOptions
{
    public string? Endpoint { get; set; }

    public string? Deployment { get; set; }

    public string? Region { get; set; }
}

public static class AiFeatures
{
    /// <summary>First candidate pilot (P3-AI): summarize a PUBLIC notice. Output is a suggestion requiring human review.</summary>
    public const string PublicNoticeSummary = "public-notice-summary";
}
