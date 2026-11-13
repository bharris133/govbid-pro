using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Auditing;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace GovBidPro.Infrastructure.Auditing;

/// <summary>
/// Writes append-only audit events to PostgreSQL and mirrors a content-free line to structured logs
/// (exported to Azure Monitor in hosted environments). Tamper-evident/immutable storage is a P1.7 item.
/// </summary>
internal sealed class DbAuditLog(IAppDbContext db, ITenantContext tenant, ICorrelationContext correlation, TimeProvider clock, ILogger<DbAuditLog> logger) : IAuditLog
{
    public async Task RecordAsync(string action, string resourceType, string? resourceId, AuditOutcome outcome, string reasonCode, CancellationToken cancellationToken)
    {
        Guid? tenantId = tenant.HasTenant ? tenant.TenantId : null;
        var entry = new AuditEvent(Guid.NewGuid(), tenantId, tenant.UserId, action, resourceType, resourceId, outcome, reasonCode, correlation.CorrelationId, clock.GetUtcNow());
        db.AuditEvents.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Audit {Action} {Outcome} {ResourceType}/{ResourceId} tenant={TenantId} actor={ActorId} reason={ReasonCode} correlation={CorrelationId}",
            action, outcome, resourceType, resourceId, tenantId, tenant.UserId, reasonCode, correlation.CorrelationId);
    }
}
