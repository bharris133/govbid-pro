using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Ai;
using GovBidPro.Application.Documents;
using GovBidPro.Application.Retention;
using GovBidPro.Application.SavedSearches;
using GovBidPro.Application.Security;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using GovBidPro.Infrastructure.Identity;
using GovBidPro.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GovBidPro.Testing;

/// <summary>Builds application services against a real PostgreSQL database for a given tenant/actor.</summary>
public sealed class TestWorld(string connectionString, MutableTimeProvider clock)
{
    public static readonly RetentionOptions DefaultRetention = new()
    {
        ApprovalStatus = "provisional (D-02); legal review pending (LEG-01)",
        Policies = new(StringComparer.Ordinal)
        {
            [RetentionPolicyKeys.ActiveDocuments] = new() { RetainWhileAccountActive = true },
            [RetentionPolicyKeys.DeletedDocumentRecovery] = new() { Days = 30 },
            [RetentionPolicyKeys.ClosedAccountExport] = new() { Days = 30 },
            [RetentionPolicyKeys.DatabaseBackups] = new() { Days = 30 },
            [RetentionPolicyKeys.SecurityAuditEvents] = new() { Days = 365 },
            [RetentionPolicyKeys.AiRequestMetadata] = new() { Days = 90 },
        },
        PurgeJob = new() { Enabled = true, BatchSize = 100 },
    };

    public string ConnectionString { get; } = connectionString;

    public MutableTimeProvider Clock { get; } = clock;

    public InMemoryObjectStorage Storage { get; } = new();

    public RetentionOptions Retention { get; set; } = DefaultRetention;

    public DocumentUploadOptions Uploads { get; set; } = new() { MaxUploadBytes = 1024 * 1024, MaxDocumentsPerTenant = 100, MalwareScanMode = "DevelopmentBypass" };

    public AiOptions Ai { get; set; } = new();

    public DataHandlingOptions DataHandling { get; set; } = new();

    public IMalwareScanner Scanner { get; set; } = new FixedMalwareScanner(ScanStatus.DevelopmentBypass);

    public GovBidProDbContext Context(ITenantContext tenant) => PostgresFixture.CreateContext(ConnectionString, tenant);

    public static TenantContextHolder Tenant(Guid tenantId, string userId, CompanyRole role = CompanyRole.CompanyAdmin)
    {
        var holder = new TenantContextHolder();
        holder.SetTenant(tenantId, userId, role);
        return holder;
    }

    /// <summary>Creates an Identity user, company and admin membership. Returns (tenantId, userId).</summary>
    public async Task<(Guid TenantId, string UserId)> CreateCompanyAsync(string name, bool aiConsent = false)
    {
        await using var db = Context(PostgresFixture.SystemContext());
        var user = new ApplicationUser { Id = Guid.NewGuid().ToString(), UserName = $"{Guid.NewGuid():N}@example.test", Email = $"{Guid.NewGuid():N}@example.test" };
        db.Users.Add(user);
        var company = new Company(Guid.NewGuid(), name, Clock.GetUtcNow());
        if (aiConsent)
        {
            company.RecordAiConsent(user.Id, Clock.GetUtcNow());
        }

        db.Companies.Add(company);
        db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), company.Id, user.Id, CompanyRole.CompanyAdmin, MembershipStatus.Active, Clock.GetUtcNow()));
        await db.SaveChangesAsync();
        return (company.Id, user.Id);
    }

    public async Task SyncRetentionAsync()
    {
        await using var db = Context(PostgresFixture.SystemContext());
        await new RetentionPolicyCatalog(db, Options.Create(Retention), Clock).SynchronizeAsync(CancellationToken.None);
    }

    public SavedSearchService SavedSearches(GovBidProDbContext db, ITenantContext tenant, RecordingAuditLog? audit = null) =>
        new(db, tenant, audit ?? new RecordingAuditLog(), Clock);

    public DocumentService Documents(GovBidProDbContext db, ITenantContext tenant, RecordingAuditLog? audit = null) =>
        new(db, tenant, audit ?? new RecordingAuditLog(), Storage, Scanner, new DocumentDataPolicyEvaluator(db, Options.Create(DataHandling)),
            new RetentionPolicyCatalog(db, Options.Create(Retention), Clock), new FixedCorrelation(), Options.Create(Uploads), Clock);

    public RetentionPurgeService Purge(GovBidProDbContext db, ITenantContext tenant) =>
        new(db, tenant, Storage, Clock, Options.Create(Retention), NullLogger<RetentionPurgeService>.Instance);

    public AiTaskService AiTasks(GovBidProDbContext db, ITenantContext tenant, IAiProvider provider, RecordingAuditLog? audit = null) =>
        new(db, tenant, new AiPolicyGate(db, tenant, Options.Create(Ai), Options.Create(DataHandling)), provider, audit ?? new RecordingAuditLog(),
            new FixedCorrelation(), Options.Create(Ai), Clock, NullLogger<AiTaskService>.Instance);

    public AiPolicyGate AiGate(GovBidProDbContext db, ITenantContext tenant) => new(db, tenant, Options.Create(Ai), Options.Create(DataHandling));

    public static UploadDocumentCommand TextUpload(string text, DataClassification classification = DataClassification.ConfidentialCommercial, bool affirm = true, string fileName = "notes.txt", string contentType = "text/plain")
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        return new UploadDocumentCommand(fileName, contentType, bytes.Length, new MemoryStream(bytes), new UploadDeclaration(classification, affirm, affirm));
    }
}
