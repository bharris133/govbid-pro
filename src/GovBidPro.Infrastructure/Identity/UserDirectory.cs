using GovBidPro.Application.Abstractions;
using GovBidPro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GovBidPro.Infrastructure.Identity;

internal sealed class UserDirectory(GovBidProDbContext db) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<string, DirectoryUser>> GetAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<string, DirectoryUser>(StringComparer.Ordinal);
        }

        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new DirectoryUser(u.Id, u.Email ?? u.UserName ?? u.Id, u.EmailConfirmed))
            .ToDictionaryAsync(u => u.UserId, StringComparer.Ordinal, cancellationToken);
    }

    public async Task<DirectoryUser?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToUpperInvariant();
        return await db.Users.AsNoTracking()
            .Where(u => u.NormalizedEmail == normalized)
            .Select(u => new DirectoryUser(u.Id, u.Email ?? u.UserName ?? u.Id, u.EmailConfirmed))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
