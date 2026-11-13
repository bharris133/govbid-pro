using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using GovBidPro.Application.Documents;
using Microsoft.Extensions.Options;

namespace GovBidPro.Infrastructure.Storage;

/// <summary>
/// Private Azure Blob Storage (D-01, CLOUD-02/03). Authenticates with managed identity; never uses
/// account keys or SAS in configuration, and never creates a publicly accessible container.
/// Not exercised by automated tests in P0 (no Azure subscription yet: gate OPS-01).
/// </summary>
internal sealed class AzureBlobObjectStorage : IObjectStorage
{
    private readonly BlobContainerClient _container;
    private bool _ensured;

    public AzureBlobObjectStorage(IOptions<StorageOptions> options)
    {
        var value = options.Value;
        if (string.IsNullOrWhiteSpace(value.BlobServiceUri))
        {
            throw new InvalidOperationException("Storage:BlobServiceUri must be configured for Azure Blob storage.");
        }

        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = value.ManagedIdentityClientId,
        });
        _container = new BlobServiceClient(new Uri(value.BlobServiceUri), credential).GetBlobContainerClient(value.ContainerName);
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        await EnsureContainerAsync(cancellationToken);
        await _container.GetBlobClient(key).UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
            },
            cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        return await _container.GetBlobClient(key).OpenReadAsync(new BlobOpenReadOptions(allowModifications: false), cancellationToken);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        await _container.GetBlobClient(key).DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
    }

    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (_ensured)
        {
            return;
        }

        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        _ensured = true;
    }
}
