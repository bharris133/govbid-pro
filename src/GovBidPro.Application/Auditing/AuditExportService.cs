using System.Globalization;
using System.Text;
using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Common;
using GovBidPro.Application.Proposals;
using GovBidPro.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GovBidPro.Application.Auditing;

/// <summary>
/// Exports a company's own audit trail as CSV for access reviews and record keeping (handoff §9D).
/// Company admins only. Rows are strictly limited to the caller's tenant; platform-level events
/// (no tenant) are never included. The export itself is audited.
/// </summary>
public sealed class AuditExportService(IAppDbContext db, ITenantContext tenant, IUserDirectory users, IAuditLog audit, TimeProvider clock)
{
    /// <summary>Longest period one export may cover.</summary>
    public const int MaxRangeDays = 366;

    /// <summary>Upper bound on rows in one export, so the response stays bounded. Narrow the dates to get the rest.</summary>
    public const int MaxRows = 50_000;

    public async Task<ExportFile> ExportCsvAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        tenant.RequireRole(CompanyRole.CompanyAdmin);
        var tenantId = tenant.RequireTenant();
        var end = to ?? clock.GetUtcNow();
        var start = from ?? end.AddDays(-90);
        if (start > end)
        {
            throw new RequestValidationException("audit.range_invalid", "The start date must be before the end date.");
        }

        if (end - start > TimeSpan.FromDays(MaxRangeDays))
        {
            throw new RequestValidationException("audit.range_too_long", $"An export can cover at most {MaxRangeDays} days.");
        }

        start = start.ToUniversalTime();
        end = end.ToUniversalTime();
        var rows = await db.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.OccurredAt >= start && e.OccurredAt <= end)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .Take(MaxRows + 1)
            .ToListAsync(cancellationToken);
        var truncated = rows.Count > MaxRows;
        if (truncated)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        // Only members of this company are named; any other actor id stays an opaque id.
        var memberIds = await db.CompanyMemberships.AsNoTracking().Where(m => m.TenantId == tenantId).Select(m => m.UserId).ToListAsync(cancellationToken);
        var names = await users.GetAsync(memberIds, cancellationToken);

        var csv = new StringBuilder();
        csv.Append("Occurred (UTC),Actor,Action,Resource type,Resource id,Outcome,Reason code,Correlation id\r\n");
        foreach (var e in rows)
        {
            var actor = e.ActorUserId is null ? "system" : names.TryGetValue(e.ActorUserId, out var user) ? user.Email : e.ActorUserId;
            csv.AppendJoin(',', new[]
            {
                e.OccurredAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                actor, e.Action, e.ResourceType, e.ResourceId, e.Outcome.ToString(), e.ReasonCode, e.CorrelationId,
            }.Select(ProposalExportService.CsvCell)).Append("\r\n");
        }

        if (truncated)
        {
            csv.Append(ProposalExportService.CsvCell($"Export stopped at {MaxRows} rows. Narrow the date range to see later events.")).Append("\r\n");
        }

        await audit.RecordAsync(AuditActions.AuditExported, "audit", null, AuditOutcome.Success, truncated ? $"rows.{rows.Count}.truncated" : $"rows.{rows.Count}", cancellationToken);
        var name = string.Create(CultureInfo.InvariantCulture, $"audit-{start:yyyyMMdd}-{end:yyyyMMdd}.csv");
        return new ExportFile(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", name);
    }
}
