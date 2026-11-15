namespace GovBidPro.Application.Abstractions;

public sealed record DirectoryUser(string UserId, string Email, bool EmailConfirmed);

/// <summary>
/// Read-only lookup of sign-in accounts, so application services can show who did something and find
/// an account by email without depending on the identity store. Callers must only pass user ids that
/// belong to the caller's own company.
/// </summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<string, DirectoryUser>> GetAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken);

    Task<DirectoryUser?> FindByEmailAsync(string email, CancellationToken cancellationToken);
}

public static class UserDirectoryExtensions
{
    /// <summary>Email for display, or a neutral placeholder for accounts that no longer exist.</summary>
    public static string NameOf(this IReadOnlyDictionary<string, DirectoryUser> users, string? userId) =>
        userId is not null && users.TryGetValue(userId, out var user) ? user.Email : "Former member";
}
