namespace GovBidPro.Application.Documents;

public sealed class DocumentUploadOptions
{
    public const string SectionName = "Documents";

    /// <summary>Maximum accepted upload size in bytes. Configured per environment.</summary>
    public long MaxUploadBytes { get; set; }

    /// <summary>Per-tenant cap on stored (non-purged) documents; a simple upload quota for P0.</summary>
    public int MaxDocumentsPerTenant { get; set; }

    /// <summary>
    /// "NotConfigured" (default; downloads denied until a scanner is configured) or
    /// "DevelopmentBypass" (local Development environment only; rejected at startup elsewhere).
    /// </summary>
    public string MalwareScanMode { get; set; } = "NotConfigured";
}
