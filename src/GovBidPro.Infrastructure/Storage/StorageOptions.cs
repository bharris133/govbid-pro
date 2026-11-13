namespace GovBidPro.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>"Local" (development) or "AzureBlob".</summary>
    public string Provider { get; set; } = "Local";

    /// <summary>Local root directory. Must be outside wwwroot; user uploads are never publicly served.</summary>
    public string? LocalRootPath { get; set; }

    /// <summary>Blob service URL, e.g. https://{account}.blob.core.windows.net. No keys: managed identity only.</summary>
    public string? BlobServiceUri { get; set; }

    public string ContainerName { get; set; } = "documents";

    /// <summary>Client id of the user-assigned managed identity, when used.</summary>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>
    /// Key Vault key used to wrap ASP.NET Core Data Protection keys (cookies/antiforgery) in Azure.
    /// Required with AzureBlob so sessions survive restarts and work across replicas.
    /// </summary>
    public string? DataProtectionKeyVaultKeyUri { get; set; }
}
