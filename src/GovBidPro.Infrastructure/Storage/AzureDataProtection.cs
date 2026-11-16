using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;

namespace GovBidPro.Infrastructure.Storage;

/// <summary>
/// Azure-specific Data Protection key storage: keys in private Blob Storage, wrapped by a Key Vault
/// key, both reached with managed identity. Kept in its own file so every Azure SDK type stays inside
/// Infrastructure adapters (see docs/cloud-portability.md).
/// </summary>
internal static class AzureDataProtection
{
    public static void Configure(IDataProtectionBuilder builder, StorageOptions storage)
    {
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = storage.ManagedIdentityClientId });
        builder
            .PersistKeysToAzureBlobStorage(new Uri($"{storage.BlobServiceUri!.TrimEnd('/')}/dataprotection/keys.xml"), credential)
            .ProtectKeysWithAzureKeyVault(new Uri(storage.DataProtectionKeyVaultKeyUri!), credential);
    }
}
