using System.Security.Cryptography;
using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Auditing;
using GovBidPro.Application.Common;
using GovBidPro.Application.Retention;
using GovBidPro.Application.Security;
using GovBidPro.Domain.Common;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GovBidPro.Application.Documents;

/// <summary>User declaration required before upload (§9B.2). A declaration is not a detection control.</summary>
public sealed record UploadDeclaration(DataClassification DeclaredClassification, bool AffirmsPermittedToUpload, bool AffirmsNoProhibitedContent);

public sealed record UploadDocumentCommand(string FileName, string ContentType, long DeclaredLength, Stream Content, UploadDeclaration Declaration, Guid? ProposalId = null, bool IsTemplate = false);

/// <summary>A new version keeps the document's classification; the uploader repeats both affirmations.</summary>
public sealed record UploadVersionCommand(string FileName, string ContentType, long DeclaredLength, Stream Content, bool AffirmsPermittedToUpload, bool AffirmsNoProhibitedContent, string? Note);

public sealed record PlaceDocumentCommand(Guid? ProposalId, bool IsTemplate);

public sealed record CopyDocumentCommand(Guid? ProposalId);

public sealed record DocumentFilter(Guid? ProposalId = null, bool TemplatesOnly = false);

public sealed record DocumentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    DataClassification Classification,
    ScanStatus ScanStatus,
    DocumentState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RecoverableUntil,
    bool IsDownloadable,
    int CurrentVersion,
    Guid? ProposalId,
    bool IsTemplate,
    DateTimeOffset UpdatedAt);

public sealed record DocumentVersionDto(int Number, string FileName, string ContentType, long SizeBytes, ScanStatus ScanStatus, string UploadedBy, DateTimeOffset UploadedAt, string? Note, bool IsCurrent, bool IsDownloadable);

public sealed record DocumentDownload(Stream Content, string FileName, string ContentType);

/// <summary>
/// Upload, versions, download and lifecycle for tenant-owned documents. Prohibited or suspected regulated
/// content is rejected before anything reaches storage (SEC-03). Every version passes the same screening.
/// </summary>
public sealed class DocumentService(
    IAppDbContext db,
    ITenantContext tenant,
    IAuditLog audit,
    IObjectStorage storage,
    IMalwareScanner scanner,
    IDataPolicyEvaluator policy,
    RetentionPolicyCatalog retention,
    ICorrelationContext correlation,
    IUserDirectory users,
    IOptions<DocumentUploadOptions> options,
    TimeProvider clock)
{
    public async Task<DocumentDto> UploadAsync(UploadDocumentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        tenant.RequireRole(CompanyRole.ProposalWriter);
        var tenantId = tenant.TenantId;
        var userId = tenant.RequireUser();

        // 1. Declaration gate: restricted/prohibited declarations are rejected without reading content.
        var declaration = command.Declaration;
        if (declaration.DeclaredClassification is DataClassification.RestrictedPendingReview or DataClassification.ProhibitedClassified)
        {
            await DenyAsync("upload.declared_restricted_or_prohibited", cancellationToken);
        }

        if (declaration.DeclaredClassification == DataClassification.Public)
        {
            // Customer uploads are never treated as PUBLIC; public notices come from ingestion only.
            await DenyAsync("upload.public_classification_not_allowed", cancellationToken);
        }

        await RequireAffirmationsAsync(declaration.AffirmsPermittedToUpload, declaration.AffirmsNoProhibitedContent, cancellationToken);
        await EnsureQuotaAsync(tenantId, cancellationToken);
        await EnsureProposalAsync(tenantId, command.ProposalId, cancellationToken);

        var screened = await ScreenAsync(command.FileName, command.ContentType, command.DeclaredLength, command.Content, cancellationToken);
        var activePolicy = await retention.GetCurrentAsync(RetentionPolicyKeys.ActiveDocuments, cancellationToken);
        var id = Guid.NewGuid();
        var storageKey = KeyFor(tenantId, id, 1);
        await PutAsync(storageKey, screened, cancellationToken);

        var now = clock.GetUtcNow();
        var fileName = FileSignatureValidator.SanitizeFileName(command.FileName);
        var document = new StoredDocument(
            id, tenantId, fileName, screened.ContentType, screened.Content.Length, screened.Sha256, storageKey,
            declaration.DeclaredClassification, userId, now, "user-upload", activePolicy.Id, screened.Scan, now, correlation.CorrelationId);
        document.Place(command.ProposalId, command.IsTemplate, now);
        db.StoredDocuments.Add(document);
        db.DocumentVersions.Add(new DocumentVersion(Guid.NewGuid(), tenantId, id, 1, fileName, screened.ContentType, screened.Content.Length, screened.Sha256, storageKey, screened.Scan, userId, now, null));
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.DocumentUpload, "document", id.ToString(), AuditOutcome.Success, "ok", cancellationToken);
        return ToDto(document);
    }

    public async Task<DocumentDto> UploadVersionAsync(Guid id, UploadVersionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        tenant.RequireRole(CompanyRole.ProposalWriter);
        var userId = tenant.RequireUser();
        var document = await FindOwnedAsync(id, cancellationToken);
        if (document.State != DocumentState.Active)
        {
            throw new ConflictException("Only active documents can get a new version.");
        }

        await RequireAffirmationsAsync(command.AffirmsPermittedToUpload, command.AffirmsNoProhibitedContent, cancellationToken);
        var screened = await ScreenAsync(command.FileName, command.ContentType, command.DeclaredLength, command.Content, cancellationToken);
        var number = document.ObjectVersion + 1;
        var storageKey = KeyFor(document.TenantId, id, number);
        var now = clock.GetUtcNow();
        DocumentVersion version;
        try
        {
            version = new DocumentVersion(Guid.NewGuid(), document.TenantId, id, number, FileSignatureValidator.SanitizeFileName(command.FileName), screened.ContentType, screened.Content.Length, screened.Sha256, storageKey, screened.Scan, userId, now, command.Note);
            document.AddVersion(version, now);
        }
        catch (DomainException ex)
        {
            throw new RequestValidationException("document.invalid", ex.Message);
        }

        await PutAsync(storageKey, screened, cancellationToken);
        db.DocumentVersions.Add(version);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race for the same version number; the stored object is orphaned and removed.
            await storage.DeleteAsync(storageKey, CancellationToken.None);
            throw new ConflictException("Someone else uploaded a version at the same time. Reload and try again.");
        }

        await audit.RecordAsync(AuditActions.DocumentVersionAdded, "document", id.ToString(), AuditOutcome.Success, $"v{number}", cancellationToken);
        return ToDto(document);
    }

    public Task<IReadOnlyList<DocumentDto>> ListAsync(CancellationToken cancellationToken) => ListAsync(new DocumentFilter(), cancellationToken);

    public async Task<IReadOnlyList<DocumentDto>> ListAsync(DocumentFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var tenantId = tenant.RequireTenant();
        var query = db.StoredDocuments.AsNoTracking().Where(d => d.TenantId == tenantId && d.State != DocumentState.Purged);
        if (filter.ProposalId is { } proposalId)
        {
            query = query.Where(d => d.ProposalId == proposalId);
        }

        if (filter.TemplatesOnly)
        {
            query = query.Where(d => d.IsTemplate);
        }

        var rows = await query.OrderByDescending(d => d.UpdatedAt).Take(500).ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<DocumentVersionDto>> ListVersionsAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await FindOwnedAsync(id, cancellationToken, tracked: false);
        var versions = await db.DocumentVersions.AsNoTracking()
            .Where(v => v.TenantId == document.TenantId && v.DocumentId == id)
            .OrderByDescending(v => v.Number)
            .ToListAsync(cancellationToken);
        var names = await users.GetAsync(versions.Select(v => v.UploadedByUserId).Distinct().ToList(), cancellationToken);
        var accessible = document.State == DocumentState.Active;
        return versions.Select(v => new DocumentVersionDto(
            v.Number, v.FileName, v.ContentType, v.SizeBytes, v.ScanStatus, names.NameOf(v.UploadedByUserId), v.UploadedAt, v.Note,
            v.Number == document.ObjectVersion, accessible && v.IsClean)).ToList();
    }

    public async Task<DocumentDownload> OpenDownloadAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await AuthorizeReadAsync(id, cancellationToken);
        var stream = await storage.OpenReadAsync(document.StorageKey, cancellationToken);
        await audit.RecordAsync(AuditActions.DocumentDownload, "document", id.ToString(), AuditOutcome.Success, "ok", cancellationToken);
        return new DocumentDownload(stream, document.FileName, document.ContentType);
    }

    /// <summary>Downloads an earlier version. The document must be readable and that version scanned clean.</summary>
    public async Task<DocumentDownload> OpenVersionDownloadAsync(Guid id, int number, CancellationToken cancellationToken)
    {
        var document = await AuthorizeReadAsync(id, cancellationToken, allowUncleanCurrent: true);
        var version = await db.DocumentVersions.AsNoTracking().SingleOrDefaultAsync(v => v.TenantId == document.TenantId && v.DocumentId == id && v.Number == number, cancellationToken)
            ?? throw new ResourceNotFoundException();
        if (!version.IsClean)
        {
            await audit.RecordAsync(AuditActions.DocumentDownload, "document", id.ToString(), AuditOutcome.Denied, "document.scan_not_clean", cancellationToken);
            throw new PolicyDeniedException("document.scan_not_clean");
        }

        var stream = await storage.OpenReadAsync(version.StorageKey, cancellationToken);
        await audit.RecordAsync(AuditActions.DocumentDownload, "document", id.ToString(), AuditOutcome.Success, $"v{number}", cancellationToken);
        return new DocumentDownload(stream, version.FileName, version.ContentType);
    }

    /// <summary>Links a document to one of the company's proposals and/or marks it as a reusable template.</summary>
    public async Task<DocumentDto> PlaceAsync(Guid id, PlaceDocumentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        tenant.RequireRole(CompanyRole.ProposalWriter);
        var document = await FindOwnedAsync(id, cancellationToken);
        await EnsureProposalAsync(document.TenantId, command.ProposalId, cancellationToken);
        try
        {
            document.Place(command.ProposalId, command.IsTemplate, clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            throw new ConflictException(ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.DocumentPlaced, "document", id.ToString(), AuditOutcome.Success, command.IsTemplate ? "template" : "document", cancellationToken);
        return ToDto(document);
    }

    /// <summary>
    /// Copies the current version of a readable document (usually a template) into a new document,
    /// optionally linked to a proposal. The bytes are identical, so the clean scan result carries over.
    /// </summary>
    public async Task<DocumentDto> CopyAsync(Guid id, CopyDocumentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        tenant.RequireRole(CompanyRole.ProposalWriter);
        var userId = tenant.RequireUser();
        var source = await AuthorizeReadAsync(id, cancellationToken);
        await EnsureQuotaAsync(source.TenantId, cancellationToken);
        await EnsureProposalAsync(source.TenantId, command.ProposalId, cancellationToken);

        byte[] bytes;
        await using (var stream = await storage.OpenReadAsync(source.StorageKey, cancellationToken))
        using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), source.Sha256, StringComparison.Ordinal))
        {
            await audit.RecordAsync(AuditActions.DocumentCopied, "document", id.ToString(), AuditOutcome.Failed, "document.integrity_mismatch", cancellationToken);
            throw new ConflictException("The stored file does not match its recorded checksum. It was not copied; contact support.");
        }

        var activePolicy = await retention.GetCurrentAsync(RetentionPolicyKeys.ActiveDocuments, cancellationToken);
        var copyId = Guid.NewGuid();
        var storageKey = KeyFor(source.TenantId, copyId, 1);
        await using (var stream = new MemoryStream(bytes, writable: false))
        {
            await storage.PutAsync(storageKey, stream, source.ContentType, cancellationToken);
        }

        var now = clock.GetUtcNow();
        var copy = new StoredDocument(
            copyId, source.TenantId, source.FileName, source.ContentType, bytes.Length, source.Sha256, storageKey,
            source.Classification, userId, now, "copy", activePolicy.Id, source.ScanStatus, now, correlation.CorrelationId);
        copy.Place(command.ProposalId, isTemplate: false, now);
        db.StoredDocuments.Add(copy);
        db.DocumentVersions.Add(new DocumentVersion(Guid.NewGuid(), source.TenantId, copyId, 1, source.FileName, source.ContentType, bytes.Length, source.Sha256, storageKey, source.ScanStatus, userId, now, $"Copied from document {id}"));
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.DocumentCopied, "document", copyId.ToString(), AuditOutcome.Success, $"from:{id}", cancellationToken);
        return ToDto(copy);
    }

    public async Task<DocumentDto> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        tenant.RequireRole(CompanyRole.ProposalWriter);
        var document = await FindOwnedAsync(id, cancellationToken);
        var recovery = await retention.GetCurrentAsync(RetentionPolicyKeys.DeletedDocumentRecovery, cancellationToken);
        var days = recovery.RetentionDays ?? throw new InvalidOperationException("Deleted-document recovery policy must define a period.");
        var now = clock.GetUtcNow();
        var recoverableUntil = now.AddDays(days);

        try
        {
            document.SoftDelete(now, recoverableUntil);
        }
        catch (DomainException ex)
        {
            throw new ConflictException(ex.Message);
        }

        db.RetentionActions.Add(new RetentionAction(
            Guid.NewGuid(), document.TenantId, "document", document.Id, document.Classification, recovery.Id, recovery.Version,
            RetentionActionType.Purge, document.LegalHold ? RetentionActionStatus.SuspendedByLegalHold : RetentionActionStatus.Scheduled,
            now, recoverableUntil, document.LegalHold, tenant.UserId, correlation.CorrelationId));
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.DocumentDelete, "document", id.ToString(), AuditOutcome.Success, "soft_deleted", cancellationToken);
        return ToDto(document);
    }

    public async Task<DocumentDto> RestoreAsync(Guid id, CancellationToken cancellationToken)
    {
        tenant.RequireRole(CompanyRole.ProposalWriter);
        var document = await FindOwnedAsync(id, cancellationToken);
        var now = clock.GetUtcNow();
        try
        {
            document.Restore(now);
        }
        catch (DomainException ex)
        {
            throw new ConflictException(ex.Message);
        }

        var pending = await db.RetentionActions
            .Where(a => a.TenantId == document.TenantId && a.ResourceId == id && a.Action == RetentionActionType.Purge && (a.Status == RetentionActionStatus.Scheduled || a.Status == RetentionActionStatus.SuspendedByLegalHold || a.Status == RetentionActionStatus.Failed))
            .ToListAsync(cancellationToken);
        foreach (var action in pending)
        {
            action.Cancel(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.DocumentRestore, "document", id.ToString(), AuditOutcome.Success, "restored", cancellationToken);
        return ToDto(document);
    }

    /// <summary>Disables all access to a document suspected of containing sensitive content (§9B.2).</summary>
    public async Task<DocumentDto> QuarantineAsync(Guid id, CancellationToken cancellationToken)
    {
        tenant.RequireRole(CompanyRole.CompanyAdmin);
        var document = await FindOwnedAsync(id, cancellationToken);
        try
        {
            document.Quarantine("reported_suspected_sensitive_content");
        }
        catch (DomainException ex)
        {
            throw new ConflictException(ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.DocumentQuarantine, "document", id.ToString(), AuditOutcome.Success, "reported_suspected_sensitive_content", cancellationToken);
        return ToDto(document);
    }

    internal static string KeyFor(Guid tenantId, Guid documentId, int version) => $"tenants/{tenantId:N}/documents/{documentId:N}/v{version}";

    private static DocumentDto ToDto(StoredDocument d) =>
        new(d.Id, d.FileName, d.ContentType, d.SizeBytes, d.Classification, d.ScanStatus, d.State, d.CreatedAt, d.RecoverableUntil, d.IsDownloadable, d.ObjectVersion, d.ProposalId, d.IsTemplate, d.UpdatedAt);

    private sealed record Screened(ReadOnlyMemory<byte> Content, string ContentType, string Sha256, ScanStatus Scan);

    /// <summary>Size cap, type allowlist with signature check, marking detection and malware screening, in that order.</summary>
    private async Task<Screened> ScreenAsync(string fileName, string contentType, long declaredLength, Stream content, CancellationToken cancellationToken)
    {
        var limits = options.Value;
        if (declaredLength <= 0 || declaredLength > limits.MaxUploadBytes)
        {
            await DenyAsync("upload.size_not_allowed", cancellationToken);
        }

        // Read into memory with a hard cap (never trust the declared length).
        var buffer = await ReadBoundedAsync(content, limits.MaxUploadBytes, cancellationToken);
        if (buffer is null)
        {
            await DenyAsync("upload.size_not_allowed", cancellationToken);
        }

        var bytes = buffer!.Value;
        var kind = FileSignatureValidator.Identify(fileName, contentType, bytes);
        if (kind is null)
        {
            await DenyAsync("upload.file_type_not_allowed", cancellationToken);
        }

        // Suspected CUI/FCI/classified markings are rejected before storage.
        var marker = ProhibitedMarkerDetector.Detect(kind!.Extension, bytes);
        if (marker is not null)
        {
            await DenyAsync($"upload.suspected_restricted_content.{marker}", cancellationToken);
        }

        // Infected content is rejected and never stored. An unreachable scanner yields Pending (rescanned later).
        var scan = await scanner.ScanAsync(bytes, fileName, cancellationToken);
        if (scan == ScanStatus.Infected)
        {
            await DenyAsync("upload.malware_detected", cancellationToken);
        }

        return new Screened(bytes, kind.ContentType, Convert.ToHexStringLower(SHA256.HashData(bytes.Span)), scan);
    }

    private async Task PutAsync(string storageKey, Screened screened, CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(screened.Content.ToArray(), writable: false);
        await storage.PutAsync(storageKey, stream, screened.ContentType, cancellationToken);
    }

    private async Task<StoredDocument> AuthorizeReadAsync(Guid id, CancellationToken cancellationToken, bool allowUncleanCurrent = false)
    {
        var tenantId = tenant.RequireTenant();
        var userId = tenant.RequireUser();
        var decision = await policy.EvaluateAsync(new DataAccessContext(tenantId, userId, ProcessingPurpose.Read), id, cancellationToken);
        if (!decision.Allowed && !(allowUncleanCurrent && decision.ReasonCode == "document.scan_not_clean"))
        {
            await audit.RecordAsync(AuditActions.DocumentDownload, "document", id.ToString(), AuditOutcome.Denied, decision.ReasonCode, cancellationToken);
            if (decision.ReasonCode == "resource.not_found_or_cross_tenant")
            {
                throw new ResourceNotFoundException();
            }

            throw new PolicyDeniedException(decision.ReasonCode);
        }

        return await db.StoredDocuments.AsNoTracking().SingleAsync(d => d.Id == id && d.TenantId == tenantId, cancellationToken);
    }

    private async Task RequireAffirmationsAsync(bool permitted, bool noProhibited, CancellationToken cancellationToken)
    {
        if (!permitted || !noProhibited)
        {
            await DenyAsync("upload.declaration_missing", cancellationToken);
        }
    }

    private async Task EnsureQuotaAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var stored = await db.StoredDocuments.CountAsync(d => d.TenantId == tenantId && d.State != DocumentState.Purged, cancellationToken);
        if (stored >= options.Value.MaxDocumentsPerTenant)
        {
            await DenyAsync("upload.quota_exceeded", cancellationToken);
        }
    }

    private async Task EnsureProposalAsync(Guid tenantId, Guid? proposalId, CancellationToken cancellationToken)
    {
        if (proposalId is { } id && !await db.Proposals.AnyAsync(p => p.Id == id && p.TenantId == tenantId, cancellationToken))
        {
            throw new RequestValidationException("document.proposal_not_found", "That proposal was not found.");
        }
    }

    private static async Task<ReadOnlyMemory<byte>?> ReadBoundedAsync(Stream content, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return total == 0 ? null : new ReadOnlyMemory<byte>(buffer.ToArray());
    }

    private async Task<StoredDocument> FindOwnedAsync(Guid id, CancellationToken cancellationToken, bool tracked = true)
    {
        var tenantId = tenant.RequireTenant();
        var query = tracked ? db.StoredDocuments : db.StoredDocuments.AsNoTracking();
        var document = await query.SingleOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId && d.State != DocumentState.Purged, cancellationToken);
        if (document is null)
        {
            await audit.RecordAsync(AuditActions.AccessDenied, "document", id.ToString(), AuditOutcome.Denied, "not_found_or_cross_tenant", cancellationToken);
            throw new ResourceNotFoundException();
        }

        return document;
    }

    private async Task DenyAsync(string reasonCode, CancellationToken cancellationToken)
    {
        await audit.RecordAsync(AuditActions.DocumentUpload, "document", null, AuditOutcome.Denied, reasonCode, cancellationToken);
        throw new PolicyDeniedException(reasonCode);
    }
}
