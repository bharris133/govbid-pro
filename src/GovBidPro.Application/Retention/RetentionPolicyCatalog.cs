using GovBidPro.Application.Abstractions;
using GovBidPro.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GovBidPro.Application.Retention;

/// <summary>
/// Materializes configured retention periods as versioned <see cref="RetentionPolicy"/> rows (RET-01).
/// A configuration change creates a new version; earlier versions are kept for audit.
/// </summary>
public sealed class RetentionPolicyCatalog(IAppDbContext db, IOptions<RetentionOptions> options, TimeProvider clock)
{
    public async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        var configured = options.Value;
        var errors = RetentionPolicyKeys.Validate(configured);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        var current = await db.RetentionPolicies.Where(p => p.SupersededAt == null).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        foreach (var (key, period) in configured.Policies)
        {
            var existing = current.SingleOrDefault(p => p.PolicyKey == key);
            if (existing is not null && existing.RetentionDays == period.Days && existing.ApprovalStatus == configured.ApprovalStatus)
            {
                continue;
            }

            var maxVersion = await db.RetentionPolicies.Where(p => p.PolicyKey == key).MaxAsync(p => (int?)p.Version, cancellationToken) ?? 0;
            existing?.Supersede(now);
            db.RetentionPolicies.Add(new RetentionPolicy(Guid.NewGuid(), key, maxVersion + 1, period.Days, configured.ApprovalStatus, now));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<RetentionPolicy> GetCurrentAsync(string policyKey, CancellationToken cancellationToken)
    {
        return await db.RetentionPolicies.AsNoTracking()
            .Where(p => p.PolicyKey == policyKey && p.SupersededAt == null)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Retention policy '{policyKey}' has not been synchronized.");
    }
}
