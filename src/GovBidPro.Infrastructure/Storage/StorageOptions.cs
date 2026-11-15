namespace GovBidPro.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Selects the object storage adapter (see docs/cloud-portability.md):
    /// "Local" (Development/Testing only), "FileSystem" (a mounted volume on any host or cloud) or
    /// "AzureBlob". Adding a provider means adding one adapter class and one entry in
    /// <see cref="StorageProviders"/>; nothing outside Infrastructure changes.
    /// </summary>
    public string Provider { get; set; } = StorageProviders.Local;

    /// <summary>
    /// Root directory for "Local" and "FileSystem". Must be outside wwwroot; user uploads are never
    /// publicly served. "FileSystem" requires an absolute path on a persistent, backed-up volume.
    /// </summary>
    public string? LocalRootPath { get; set; }

    /// <summary>
    /// Directory for ASP.NET Core Data Protection keys when not using a cloud key store. Required with
    /// "FileSystem" so sign-in cookies survive restarts; must be a private, persistent volume.
    /// </summary>
    public string? DataProtectionKeyDirectory { get; set; }

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

/// <summary>Known object storage provider names. Cloud-specific adapters live in Infrastructure only.</summary>
public static class StorageProviders
{
    public const string Local = "Local";
    public const string FileSystem = "FileSystem";
    public const string AzureBlob = "AzureBlob";

    public static bool IsKnown(string? provider) => provider is Local or FileSystem or AzureBlob;
}
