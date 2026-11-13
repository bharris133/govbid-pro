using System.Security.Cryptography;
using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Auditing;
using GovBidPro.Application.Common;
using GovBidPro.Application.Retention;
using GovBidPro.Application.Security;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GovBidPro.Application.Documents;

/// <summary>User declaration required before upload (§9B.2). A declaration is not a detection control.</summary>
public sealed record UploadDeclaration(DataClassification DeclaredClassification, bool AffirmsPermittedToUpload, bool AffirmsNoProhibitedContent);

public sealed record UploadDocumentCommand(string FileName, string ContentType, long DeclaredLength, Stream Content, UploadDeclaration Declaration);

public sealed record DocumentDto(Guid Id, string FileName, string ContentType, long SizeBytes, DataClassification Classification, ScanStatus ScanStatus, DocumentState State, DateTimeOffset CreatedAt, DateTimeOffset? RecoverableUntil, bool IsDownloadable);

public sealed record DocumentDownload(Stream Content, string FileName, string ContentType);

/// <summary>
/// Upload, download and lifecycle for tenant-owned documents. Prohibited or suspected regulated
/// content is rejected before anything reaches storage (SEC-03).
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
    IOptions<DocumentUploadOptions> options,
    TimeProvider clock)
{
    public async Task<DocumentDto> UploadAsync(UploadDocumentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        tenant.RequireRole(CompanyRole.ProposalWriter);
        var tenantId = tenant.TenantId;
        var userId = tenant.RequireUser();
        var limits = options.Value;

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

        if (!declaration.AffirmsPermittedToUpload || !declaration.AffirmsNoProhibitedContent)
        {
            await DenyAsync("upload.declaration_missing", cancellationToken);
        }

        if (command.DeclaredLength <= 0 || command.DeclaredLength > limits.MaxUploadBytes)
        {
            await DenyAsync("upload.size_not_allowed", cancellationToken);
        }

        var stored = await db.StoredDocuments.CountAsync(d => d.TenantId == tenantId && d.State != DocumentState.Purged, cancellationToken);
        if (stored >= limits.MaxDocumentsPerTenant)
        {
            await DenyAsync("upload.quota_exceeded", cancellationToken);
        }

        // 2. Read into memory with a hard cap (never trust the declared length).
        var buffer = await ReadBoundedAsync(command.Content, limits.MaxUploadBytes, cancellationToken);
        if (buffer is null)
        {
            await DenyAsync("upload.size_not_allowed", cancellationToken);
        }

        var content = buffer!.Value;

        // 3. Type allowlist by extension + declared type + signature.
        var kind = FileSignatureValidator.Identify(command.FileName, command.ContentType, content);
        if (kind is null)
        {
            await DenyAsync("upload.file_type_not_allowed", cancellationToken);
        }

        // 4. Marking detection: suspected CUI/FCI/classified is rejected before storage.
        var marker = ProhibitedMarkerDetector.Detect(kind!.Extension, content);
        if (marker is not null)
        {
            await DenyAsync($"upload.suspected_restricted_content.{marker}", cancellationToken);
        }

        // 5. Malware screening. Infected content is rejected and never stored.
        var scan = await scanner.ScanAsync(content, command.FileName, cancellationToken);
        if (scan == ScanStatus.Infected)
        {
            await DenyAsync("upload.malware_detected", cancellationToken);
        }

        var activePolicy = await retention.GetCurrentAsync(RetentionPolicyKeys.ActiveDocuments, cancellationToken);
        var id = Guid.NewGuid();
        var storageKey = $"tenants/{tenantId:N}/documents/{id:N}/v1";
        var hash = Convert.ToHexStringLower(SHA256.HashData(content.Span));
        await using (var stream = new MemoryStream(content.ToArray(), writable: false))
        {
            await storage.PutAsync(storageKey, stream, kind.ContentType, cancellationToken);
        }

        var now = clock.GetUtcNow();
        var document = new StoredDocument(
            id, tenantId, FileSignatureValidator.SanitizeFileName(command.FileName), kind.ContentType, content.Length, hash, storageKey,
            declaration.DeclaredClassification, userId, now, "user-upload", activePolicy.Id, scan, now, correlation.CorrelationId);
        db.StoredDocuments.Add(document);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.DocumentUpload, "document", id.ToString(), AuditOutcome.Success, "ok", cancellationToken);
        return ToDto(document);
    }

    public async Task<IReadOnlyList<DocumentDto>> ListAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        var rows = await db.StoredDocuments.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.State != DocumentState.Purged)
            .OrderByDescending(d => d.CreatedAt)
            .Take(500)
            .ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<DocumentDownload> OpenDownloadAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        var userId = tenant.RequireUser();
        var decision = await policy.EvaluateAsync(new DataAccessContext(tenantId, userId, ProcessingPurpose.Read), id, cancellationToken);
        if (!decision.Allowed)
        {
            await audit.RecordAsync(AuditActions.DocumentDownload, "document", id.ToString(), AuditOutcome.Denied, decision.ReasonCode, cancellationToken);
            if (decision.ReasonCode == "resource.not_found_or_cross_tenant")
            {
                throw new ResourceNotFoundException();
            }

            throw new PolicyDeniedException(decision.ReasonCode);
        }

        var document = await db.StoredDocuments.AsNoTracking().SingleAsync(d => d.Id == id && d.TenantId == tenantId, cancellationToken);
        var stream = await storage.OpenReadAsync(document.StorageKey, cancellationToken);
        await audit.RecordAsync(AuditActions.DocumentDownload, "document", id.ToString(), AuditOutcome.Success, "ok", cancellationToken);
        return new DocumentDownload(stream, document.FileName, document.ContentType);
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
        catch (Domain.Common.DomainException ex)
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
        catch (Domain.Common.DomainException ex)
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
        catch (Domain.Common.DomainException ex)
        {
            throw new ConflictException(ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.DocumentQuarantine, "document", id.ToString(), AuditOutcome.Success, "reported_suspected_sensitive_content", cancellationToken);
        return ToDto(document);
    }

    private static DocumentDto ToDto(StoredDocument d) =>
        new(d.Id, d.FileName, d.ContentType, d.SizeBytes, d.Classification, d.ScanStatus, d.State, d.CreatedAt, d.RecoverableUntil, d.IsDownloadable);

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

    private async Task<StoredDocument> FindOwnedAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId;
        var document = await db.StoredDocuments.SingleOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId && d.State != DocumentState.Purged, cancellationToken);
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
