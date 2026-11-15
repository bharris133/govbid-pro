using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Ai;
using GovBidPro.Application.Assessments;
using GovBidPro.Application.Documents;
using GovBidPro.Application.Opportunities;
using GovBidPro.Application.Retention;
using GovBidPro.Application.SavedSearches;
using GovBidPro.Application.Security;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using GovBidPro.Infrastructure.Identity;
using GovBidPro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
        var email = $"{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { Id = Guid.NewGuid().ToString(), UserName = email, Email = email, NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant() };
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

    /// <summary>Imports public notices through the real import service (system context).</summary>
    public async Task ImportAsync(params ImportedOpportunity[] notices)
    {
        await using var db = Context(PostgresFixture.SystemContext());
        var summary = await new OpportunityImportService(db, Clock, NullLogger<OpportunityImportService>.Instance)
            .RunAsync(new ListOpportunitySource(notices), maxPages: 5, CancellationToken.None);
        if (summary.Status != ImportRunStatus.Succeeded)
        {
            throw new InvalidOperationException($"Test import failed: {summary.Status}.");
        }
    }

    public ImportedOpportunity Notice(string id, string title = "Request for Proposal: Test services", int dueInDays = 20, string naics = "541511", string? state = "VA", string? setAside = null, string? agency = "Example Agency", SourceNoticeType type = SourceNoticeType.Solicitation, string? description = null) => new()
    {
        ExternalId = id,
        Title = title,
        Description = description,
        Agency = agency,
        NoticeType = type,
        SourceNoticeTypeRaw = type.ToString(),
        NaicsCode = naics,
        SetAside = setAside,
        PlaceOfPerformanceState = state,
        PostedAt = Clock.GetUtcNow().AddDays(-1),
        ResponseDueAt = Clock.GetUtcNow().AddDays(dueInDays),
        ResponseDueOriginalText = "See solicitation",
        ResponseDueTimeZone = "America/New_York",
        SubmissionInstructions = "Submit as instructed in the solicitation (test).",
        SourceUrl = $"https://example.invalid/test/{id}",
    };

    public async Task<Guid> OpportunityIdAsync(string externalId)
    {
        await using var db = Context(PostgresFixture.SystemContext());
        return await db.Opportunities.Where(o => o.ExternalId == externalId).Select(o => o.Id).SingleAsync();
    }

    /// <summary>Adds another active member to an existing company. Returns the new user's id.</summary>
    public async Task<string> AddMemberAsync(Guid tenantId, CompanyRole role, string? email = null)
    {
        await using var db = Context(PostgresFixture.SystemContext());
        email ??= $"{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { Id = Guid.NewGuid().ToString(), UserName = email, Email = email, NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant() };
        db.Users.Add(user);
        db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), tenantId, user.Id, role, MembershipStatus.Active, Clock.GetUtcNow()));
        await db.SaveChangesAsync();
        return user.Id;
    }

    public async Task SyncRetentionAsync()
    {
        await using var db = Context(PostgresFixture.SystemContext());
        await new RetentionPolicyCatalog(db, Options.Create(Retention), Clock).SynchronizeAsync(CancellationToken.None);
    }

    public SavedSearchService SavedSearches(GovBidProDbContext db, ITenantContext tenant, RecordingAuditLog? audit = null) =>
        new(db, tenant, audit ?? new RecordingAuditLog(), Clock);

    public AssessmentOptions Assessment { get; set; } = new();

    public AssessmentService Assessments(GovBidProDbContext db, ITenantContext tenant, RecordingAuditLog? audit = null) =>
        new(db, tenant, audit ?? new RecordingAuditLog(), new DbUserDirectory(db), Options.Create(Assessment), Clock);

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

/// <summary>Single-page in-memory source for tests. Not live data.</summary>
public sealed class ListOpportunitySource(IReadOnlyList<ImportedOpportunity> items, string key = "test") : IOpportunitySource
{
    public string SourceKey => key;

    public bool IsLive => false;

    public Task<OpportunityImportPage> FetchAsync(OpportunityImportCursor? cursor, CancellationToken cancellationToken) =>
        Task.FromResult(new OpportunityImportPage(items, null));
}

/// <summary>User lookup over the test database (mirrors the production adapter).</summary>
public sealed class DbUserDirectory(GovBidProDbContext db) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<string, DirectoryUser>> GetAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new DirectoryUser(u.Id, u.Email ?? u.Id, u.EmailConfirmed))
            .ToDictionaryAsync(u => u.UserId, StringComparer.Ordinal, cancellationToken);

    public async Task<DirectoryUser?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToUpperInvariant();
        return await db.Users.AsNoTracking().Where(u => u.NormalizedEmail == normalized)
            .Select(u => new DirectoryUser(u.Id, u.Email ?? u.Id, u.EmailConfirmed)).SingleOrDefaultAsync(cancellationToken);
    }
}
