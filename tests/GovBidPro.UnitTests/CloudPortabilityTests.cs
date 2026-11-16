using System.Reflection;
using GovBidPro.Application.Abstractions;
using GovBidPro.Domain.Entities;

namespace GovBidPro.UnitTests;

/// <summary>
/// Cloud portability guardrail (docs/cloud-portability.md): cloud SDKs may only be used by adapters in
/// GovBidPro.Infrastructure. Domain, Application and Web must stay provider-neutral so that changing
/// cloud provider means writing adapters and changing configuration, not rewriting features.
/// </summary>
public sealed class CloudPortabilityTests
{
    private static readonly string[] CloudSdkPrefixes = ["Azure.", "Microsoft.Azure", "Amazon.", "AWSSDK", "Google.Cloud"];

    [Theory]
    [InlineData(typeof(Company))]
    [InlineData(typeof(IAppDbContext))]
    public void Domain_and_Application_assemblies_do_not_reference_cloud_sdks(Type marker)
    {
        var references = marker.Assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);
        Assert.DoesNotContain(references, name => CloudSdkPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Cloud_sdk_namespaces_are_only_imported_by_infrastructure_adapters()
    {
        var root = FindRepositoryRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => File.ReadLines(file).Any(line => line.StartsWith("using Azure", StringComparison.Ordinal) || line.StartsWith("using Amazon", StringComparison.Ordinal) || line.StartsWith("using Google.Cloud", StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(file => !file.StartsWith("src/GovBidPro.Infrastructure/", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void Cloud_sdk_packages_are_only_referenced_by_the_infrastructure_project()
    {
        var root = FindRepositoryRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(file => !file.EndsWith("GovBidPro.Infrastructure.csproj", StringComparison.Ordinal))
            .Where(file => File.ReadLines(file).Any(line => CloudSdkPrefixes.Any(p => line.Contains($"PackageReference Include=\"{p}", StringComparison.OrdinalIgnoreCase))))
            .ToList();
        Assert.Empty(offenders);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GovBidPro.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
