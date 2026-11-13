using Azure.Identity;
using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Ai;
using GovBidPro.Application.Auditing;
using GovBidPro.Application.Companies;
using GovBidPro.Application.Documents;
using GovBidPro.Application.Opportunities;
using GovBidPro.Application.Retention;
using GovBidPro.Application.SavedSearches;
using GovBidPro.Application.Security;
using GovBidPro.Infrastructure.Ai;
using GovBidPro.Infrastructure.Auditing;
using GovBidPro.Infrastructure.Persistence;
using GovBidPro.Infrastructure.SamGov;
using GovBidPro.Infrastructure.Scanning;
using GovBidPro.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GovBidPro.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "GovBidPro";
    public const string TestingEnvironment = "Testing";

    public static IServiceCollection AddGovBidProCore(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<TenantContextHolder>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContextHolder>());

        services.AddDbContext<GovBidProDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} is not configured.");
            }

            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3));
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<GovBidProDbContext>());

        services.AddOptions<RetentionOptions>().Bind(configuration.GetSection(RetentionOptions.SectionName))
            .Validate(o => RetentionPolicyKeys.Validate(o).Count == 0, "Retention configuration is incomplete; see RetentionPolicyKeys.Validate.")
            .ValidateOnStart();
        services.AddOptions<DocumentUploadOptions>().Bind(configuration.GetSection(DocumentUploadOptions.SectionName))
            .Validate(o => o.MaxUploadBytes > 0 && o.MaxDocumentsPerTenant > 0, "Documents:MaxUploadBytes and MaxDocumentsPerTenant must be configured.")
            .Validate(o => o.MalwareScanMode is "NotConfigured" || (o.MalwareScanMode is "DevelopmentBypass" && (environment.IsDevelopment() || environment.IsEnvironment(TestingEnvironment))), "Documents:MalwareScanMode DevelopmentBypass is only allowed in Development/Testing.")
            .ValidateOnStart();
        services.AddOptions<DataHandlingOptions>().Bind(configuration.GetSection(DataHandlingOptions.SectionName));
        services.AddOptions<AiOptions>().Bind(configuration.GetSection(AiOptions.SectionName))
            .Validate(o => !o.PlatformEnabled || !string.Equals(o.Provider, "Fake", StringComparison.OrdinalIgnoreCase) || environment.IsEnvironment(TestingEnvironment), "Ai:Provider Fake is only allowed in the Testing environment.")
            .Validate(o => !o.PlatformEnabled || string.Equals(o.Provider, "Fake", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(o.Ai01ApprovalReference), "Ai:PlatformEnabled requires a recorded AI-01 approval reference.")
            .ValidateOnStart();
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.SectionName))
            .Validate(o => o.Provider is "AzureBlob" || (o.Provider is "Local" && (environment.IsDevelopment() || environment.IsEnvironment(TestingEnvironment))), "Storage:Provider must be AzureBlob outside Development/Testing.")
            .Validate(o => o.Provider is not "AzureBlob" || (!string.IsNullOrWhiteSpace(o.BlobServiceUri) && !string.IsNullOrWhiteSpace(o.DataProtectionKeyVaultKeyUri)), "Storage:BlobServiceUri and Storage:DataProtectionKeyVaultKeyUri are required for AzureBlob.")
            .ValidateOnStart();

        // Data Protection keys (auth cookies, antiforgery) are shared across replicas in Azure:
        // persisted to private Blob Storage and wrapped with a Key Vault key, via managed identity.
        var storageSection = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
        var dataProtection = services.AddDataProtection().SetApplicationName("GovBidPro");
        if (storageSection.Provider == "AzureBlob" && !string.IsNullOrWhiteSpace(storageSection.BlobServiceUri) && !string.IsNullOrWhiteSpace(storageSection.DataProtectionKeyVaultKeyUri))
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = storageSection.ManagedIdentityClientId });
            dataProtection
                .PersistKeysToAzureBlobStorage(new Uri($"{storageSection.BlobServiceUri.TrimEnd('/')}/dataprotection/keys.xml"), credential)
                .ProtectKeysWithAzureKeyVault(new Uri(storageSection.DataProtectionKeyVaultKeyUri), credential);
        }

        services.AddSingleton<IObjectStorage>(sp =>
        {
            var storage = sp.GetRequiredService<IOptions<StorageOptions>>();
            return storage.Value.Provider == "AzureBlob" ? new AzureBlobObjectStorage(storage) : new LocalFileObjectStorage(storage);
        });
        services.AddSingleton<IMalwareScanner>(sp =>
            sp.GetRequiredService<IOptions<DocumentUploadOptions>>().Value.MalwareScanMode == "DevelopmentBypass"
                ? new DevelopmentBypassMalwareScanner()
                : new NotConfiguredMalwareScanner());

        services.AddScoped<IAiProvider>(sp =>
        {
            var ai = sp.GetRequiredService<IOptions<AiOptions>>();
            return string.Equals(ai.Value.Provider, "Azure", StringComparison.OrdinalIgnoreCase) ? new AzureAiProvider(ai) : new DisabledAiProvider();
        });

        services.AddScoped<IAuditLog, DbAuditLog>();
        services.AddScoped<IDataPolicyEvaluator, DocumentDataPolicyEvaluator>();
        services.AddScoped<IAiPolicyGate, AiPolicyGate>();
        services.AddScoped<AiTaskService>();
        services.AddScoped<CompanyService>();
        services.AddScoped<OpportunityQueryService>();
        services.AddScoped<OpportunityImportService>();
        services.AddScoped<SavedSearchService>();
        services.AddScoped<DocumentService>();
        services.AddScoped<RetentionPolicyCatalog>();
        services.AddScoped<RetentionPurgeService>();
        services.AddSingleton<FixtureOpportunitySource>();
        return services;
    }
}
