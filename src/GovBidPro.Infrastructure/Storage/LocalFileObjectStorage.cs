using System.Text.RegularExpressions;
using GovBidPro.Application.Documents;
using Microsoft.Extensions.Options;

namespace GovBidPro.Infrastructure.Storage;

/// <summary>Development storage on the local file system, outside the web root.</summary>
internal sealed partial class LocalFileObjectStorage : IObjectStorage
{
    private readonly string _root;

    public LocalFileObjectStorage(IOptions<StorageOptions> options)
    {
        var root = options.Value.LocalRootPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException("Storage:LocalRootPath must be configured for local storage.");
        }

        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        File.Move(temp, path, overwrite: false);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        Stream stream = new FileStream(Resolve(key), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string Resolve(string key)
    {
        if (!SafeKey().IsMatch(key))
        {
            throw new ArgumentException("Invalid storage key.", nameof(key));
        }

        var path = Path.GetFullPath(Path.Combine(_root, key));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid storage key.", nameof(key));
        }

        return path;
    }

    [GeneratedRegex("^[a-z0-9]+(/[a-z0-9]+)*$")]
    private static partial Regex SafeKey();
}
