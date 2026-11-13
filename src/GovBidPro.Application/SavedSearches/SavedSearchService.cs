using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Auditing;
using GovBidPro.Application.Common;
using GovBidPro.Domain.Common;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GovBidPro.Application.SavedSearches;

public sealed record SavedSearchDto(Guid Id, string Name, OpportunitySearchCriteria Criteria, NotificationFrequency NotificationFrequency, DateTimeOffset CreatedAt);

public sealed record CreateSavedSearchCommand(string Name, OpportunitySearchCriteria Criteria, NotificationFrequency NotificationFrequency);

/// <summary>Tenant-owned saved searches. Every query is explicitly filtered by the resolved tenant.</summary>
public sealed class SavedSearchService(IAppDbContext db, ITenantContext tenant, IAuditLog audit, TimeProvider clock)
{
    public const int MaxSavedSearchesPerTenant = 200;

    public async Task<IReadOnlyList<SavedSearchDto>> ListAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        var rows = await db.SavedSearches.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<SavedSearchDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        var row = await db.SavedSearches.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, cancellationToken);
        if (row is null)
        {
            await audit.RecordAsync(AuditActions.AccessDenied, "saved_search", id.ToString(), AuditOutcome.Denied, "not_found_or_cross_tenant", cancellationToken);
            throw new ResourceNotFoundException();
        }

        return ToDto(row);
    }

    public async Task<SavedSearchDto> CreateAsync(CreateSavedSearchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        tenant.RequireRole(CompanyRole.ProposalWriter);
        var tenantId = tenant.TenantId;
        var userId = tenant.RequireUser();

        var count = await db.SavedSearches.CountAsync(s => s.TenantId == tenantId, cancellationToken);
        if (count >= MaxSavedSearchesPerTenant)
        {
            throw new RequestValidationException("saved_search.limit", "Saved search limit reached.");
        }

        SavedSearch entity;
        try
        {
            entity = new SavedSearch(Guid.NewGuid(), tenantId, command.Name, command.Criteria ?? new OpportunitySearchCriteria(), command.NotificationFrequency, userId, clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            throw new RequestValidationException("saved_search.invalid", ex.Message);
        }

        db.SavedSearches.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.SavedSearchCreated, "saved_search", entity.Id.ToString(), AuditOutcome.Success, "ok", cancellationToken);
        return ToDto(entity);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        tenant.RequireRole(CompanyRole.ProposalWriter);
        var tenantId = tenant.TenantId;
        var entity = await db.SavedSearches.SingleOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, cancellationToken);
        if (entity is null)
        {
            await audit.RecordAsync(AuditActions.AccessDenied, "saved_search", id.ToString(), AuditOutcome.Denied, "not_found_or_cross_tenant", cancellationToken);
            throw new ResourceNotFoundException();
        }

        db.SavedSearches.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.SavedSearchDeleted, "saved_search", id.ToString(), AuditOutcome.Success, "ok", cancellationToken);
    }

    private static SavedSearchDto ToDto(SavedSearch s) => new(s.Id, s.Name, s.Criteria, s.NotificationFrequency, s.CreatedAt);
}
