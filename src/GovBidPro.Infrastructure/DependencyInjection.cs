using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Ai;
using GovBidPro.Application.Assessments;
using GovBidPro.Application.Auditing;
using GovBidPro.Application.Companies;
using GovBidPro.Application.Dashboard;
using GovBidPro.Application.Documents;
using GovBidPro.Application.Opportunities;
using GovBidPro.Application.Proposals;
using GovBidPro.Application.Retention;
using GovBidPro.Application.SavedSearches;
using GovBidPro.Application.Security;
using GovBidPro.Infrastructure.Ai;
using GovBidPro.Infrastructure.Auditing;
using GovBidPro.Infrastructure.Identity;
using GovBidPro.Infrastructure.Persistence;
using GovBidPro.Infrastructure.SamGov;
using GovBidPro.Infrastructure.Scanning;
using GovBidPro.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
            .Validate(o => o.MalwareScanMode is "NotConfigured" or "ClamAv" || (o.MalwareScanMode is "DevelopmentBypass" && (environment.IsDevelopment() || environment.IsEnvironment(TestingEnvironment))), "Documents:MalwareScanMode must be NotConfigured, ClamAv or (Development/Testing only) DevelopmentBypass.")
            .ValidateOnStart();
        var scanMode = configuration[$"{DocumentUploadOptions.SectionName}:MalwareScanMode"];
        services.AddOptions<ClamAvOptions>().Bind(configuration.GetSection(ClamAvOptions.SectionName))
            .Validate(o => scanMode != "ClamAv" || ClamAvOptions.IsValid(o), "Documents:ClamAv:Host is required when Documents:MalwareScanMode is ClamAv.")
            .ValidateOnStart();
        services.AddOptions<DocumentRescanOptions>().Bind(configuration.GetSection(DocumentRescanOptions.SectionName))
            .Validate(o => o.IntervalMinutes is >= 1 and <= 1440 && o.BatchSize is >= 1 and <= 500, "Documents:Rescan interval or batch size is out of range.")
            .ValidateOnStart();
        services.AddOptions<DataHandlingOptions>().Bind(configuration.GetSection(DataHandlingOptions.SectionName));
        services.AddOptions<AiOptions>().Bind(configuration.GetSection(AiOptions.SectionName))
            .Validate(o => !o.PlatformEnabled || !string.Equals(o.Provider, "Fake", StringComparison.OrdinalIgnoreCase) || environment.IsEnvironment(TestingEnvironment), "Ai:Provider Fake is only allowed in the Testing environment.")
            .Validate(o => !o.PlatformEnabled || string.Equals(o.Provider, "Fake", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(o.Ai01ApprovalReference), "Ai:PlatformEnabled requires a recorded AI-01 approval reference.")
            .ValidateOnStart();
        var isLocalEnvironment = environment.IsDevelopment() || environment.IsEnvironment(TestingEnvironment);
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.SectionName))
            .Validate(o => StorageProviders.IsKnown(o.Provider), "Storage:Provider must be Local, FileSystem or AzureBlob.")
            .Validate(o => o.Provider is not StorageProviders.Local || isLocalEnvironment, "Storage:Provider Local is only allowed in Development/Testing; use FileSystem or a cloud provider.")
            .Validate(o => o.Provider is not StorageProviders.FileSystem || (Path.IsPathFullyQualified(o.LocalRootPath ?? string.Empty) && Path.IsPathFullyQualified(o.DataProtectionKeyDirectory ?? string.Empty)), "Storage:LocalRootPath and Storage:DataProtectionKeyDirectory must be absolute paths for FileSystem.")
            .Validate(o => o.Provider is not StorageProviders.AzureBlob || (!string.IsNullOrWhiteSpace(o.BlobServiceUri) && !string.IsNullOrWhiteSpace(o.DataProtectionKeyVaultKeyUri)), "Storage:BlobServiceUri and Storage:DataProtectionKeyVaultKeyUri are required for AzureBlob.")
            .ValidateOnStart();

        // Data Protection keys (auth cookies, antiforgery) must be shared across replicas and survive
        // restarts. Azure: private Blob Storage wrapped with a Key Vault key via managed identity.
        // Any other host: a private persistent directory (Storage:DataProtectionKeyDirectory).
        var storageSection = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
        var dataProtection = services.AddDataProtection().SetApplicationName("GovBidPro");
        if (storageSection.Provider == StorageProviders.AzureBlob && !string.IsNullOrWhiteSpace(storageSection.BlobServiceUri) && !string.IsNullOrWhiteSpace(storageSection.DataProtectionKeyVaultKeyUri))
        {
            AzureDataProtection.Configure(dataProtection, storageSection);
        }
        else if (!string.IsNullOrWhiteSpace(storageSection.DataProtectionKeyDirectory))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(storageSection.DataProtectionKeyDirectory));
        }

        services.AddSingleton<IObjectStorage>(sp =>
        {
            var storage = sp.GetRequiredService<IOptions<StorageOptions>>();
            return storage.Value.Provider switch
            {
                StorageProviders.AzureBlob => new AzureBlobObjectStorage(storage),
                StorageProviders.Local or StorageProviders.FileSystem => new LocalFileObjectStorage(storage),
                _ => throw new InvalidOperationException("Unknown Storage:Provider."),
            };
        });
        services.AddSingleton<IMalwareScanner>(sp => sp.GetRequiredService<IOptions<DocumentUploadOptions>>().Value.MalwareScanMode switch
        {
            "DevelopmentBypass" => new DevelopmentBypassMalwareScanner(),
            "ClamAv" => ActivatorUtilities.CreateInstance<ClamAvMalwareScanner>(sp),
            _ => new NotConfiguredMalwareScanner(),
        });
        services.AddScoped<DocumentRescanService>();

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
        services.AddScoped<DashboardService>();
        services.AddOptions<AssessmentOptions>().Bind(configuration.GetSection(AssessmentOptions.SectionName));
        services.AddScoped<AssessmentService>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<MemberService>();
        services.AddOptions<ProposalOptions>().Bind(configuration.GetSection(ProposalOptions.SectionName))
            .Validate(ProposalOptions.IsValid, "Proposals settings are invalid: StageLabels and SectionTemplates keys must be known statuses/response types with short non-empty values.")
            .ValidateOnStart();
        services.TryAddScoped<IProposalNotifier, NoOpProposalNotifier>();
        services.AddScoped<ProposalService>();
        services.AddScoped<ProposalExportService>();
        services.AddScoped<OpportunityImportService>();
        services.AddScoped<SavedSearchService>();
        services.AddScoped<DocumentService>();
        services.AddScoped<RetentionPolicyCatalog>();
        services.AddScoped<RetentionPurgeService>();
        services.AddSingleton<FixtureOpportunitySource>();

        // Live sources are registered as scheduled IOpportunitySource only when enabled. The SAM.gov
        // client has request logging removed because the documented API takes the key in the query string.
        services.AddOptions<SamGovOptions>().Bind(configuration.GetSection(SamGovOptions.SectionName))
            .Validate(SamGovOptions.IsValid, "OpportunitySources:SamGov is enabled but ApiKey, an https BaseUrl, PageSize (1-1000), LookbackDays (1-364), RequestTimeoutSeconds (5-120) or OffsetMode is invalid.")
            .ValidateOnStart();
        services.AddOptions<OpportunitySyncOptions>().Bind(configuration.GetSection(OpportunitySyncOptions.SectionName))
            .Validate(o => o.IntervalMinutes >= 5 && o.MaxPagesPerRun is >= 1 and <= 100, "OpportunitySources:Sync IntervalMinutes must be at least 5 and MaxPagesPerRun 1-100.")
            .ValidateOnStart();
        services.AddHttpClient(SamGovOpportunitySource.HttpClientName).RemoveAllLoggers();
        if (configuration.GetSection(SamGovOptions.SectionName).GetValue<bool>(nameof(SamGovOptions.Enabled)))
        {
            services.AddScoped<IOpportunitySource>(sp => new SamGovOpportunitySource(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(SamGovOpportunitySource.HttpClientName),
                sp.GetRequiredService<IOptions<SamGovOptions>>(),
                sp.GetRequiredService<TimeProvider>()));
        }

        services.AddScoped<OpportunitySyncService>();
        return services;
    }
}
