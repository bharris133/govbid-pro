using GovBidPro.Domain.Common;
using GovBidPro.Domain.Enums;

namespace GovBidPro.Domain.Entities;

/// <summary>An in-app message for one member of one company. The link is always a path inside the app.</summary>
public sealed class Notification : ITenantOwned
{
    private Notification()
    {
        UserId = string.Empty;
        Kind = string.Empty;
        Title = string.Empty;
    }

    public Notification(Guid id, Guid tenantId, string userId, string kind, string title, string? body, string? link, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (tenantId == Guid.Empty)
        {
            throw new DomainException("Tenant id is required.");
        }

        if (link is not null && (!link.StartsWith('/') || link.StartsWith("//", StringComparison.Ordinal) || link.Contains('\\', StringComparison.Ordinal)))
        {
            throw new DomainException("Notification links must be paths inside the application.");
        }

        Id = id;
        TenantId = tenantId;
        UserId = userId;
        Kind = kind;
        Title = Text.Required(title, 200, "Title");
        Body = Text.Optional(body, 1000, "Body");
        Link = Text.Optional(link, 300, "Link");
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string UserId { get; private set; }

    public string Kind { get; private set; }

    public string Title { get; private set; }

    public string? Body { get; private set; }

    public string? Link { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public void MarkRead(DateTimeOffset at) => ReadAt ??= at;
}

public enum WorkTaskStatus
{
    Open = 0,
    InProgress = 1,
    Done = 2,
    Cancelled = 3,
}

/// <summary>A piece of work for a member, optionally tied to a proposal.</summary>
public sealed class WorkTask : ITenantOwned
{
    private WorkTask()
    {
        Title = string.Empty;
        CreatedByUserId = string.Empty;
    }

    public WorkTask(Guid id, Guid tenantId, Guid? proposalId, string title, string? description, string? assigneeUserId, DateOnly? dueOn, string createdByUserId, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(createdByUserId);
        if (tenantId == Guid.Empty)
        {
            throw new DomainException("Tenant id is required.");
        }

        Id = id;
        TenantId = tenantId;
        ProposalId = proposalId;
        Title = string.Empty;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
        Status = WorkTaskStatus.Open;
        Update(title, description, assigneeUserId, dueOn, createdAt);
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid? ProposalId { get; private set; }

    public string Title { get; private set; }

    public string? Description { get; private set; }

    public string? AssigneeUserId { get; private set; }

    public DateOnly? DueOn { get; private set; }

    public WorkTaskStatus Status { get; private set; }

    public string CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public bool IsOpen => Status is WorkTaskStatus.Open or WorkTaskStatus.InProgress;

    public void Update(string title, string? description, string? assigneeUserId, DateOnly? dueOn, DateTimeOffset at)
    {
        Title = Text.Required(title, 200, "Task title");
        Description = Text.Optional(description, 2000, "Description");
        AssigneeUserId = string.IsNullOrWhiteSpace(assigneeUserId) ? null : assigneeUserId;
        DueOn = dueOn;
        UpdatedAt = at;
    }

    public void SetStatus(WorkTaskStatus status, DateTimeOffset at)
    {
        if (!Enum.IsDefined(status))
        {
            throw new DomainException("Unknown task status.");
        }

        Status = status;
        CompletedAt = status == WorkTaskStatus.Done ? at : null;
        UpdatedAt = at;
    }
}

/// <summary>A comment on a proposal, optionally about one requirement. Deleting hides the text but keeps the record.</summary>
public sealed class Comment : ITenantOwned
{
    public const int MaxBodyLength = 4000;

    private Comment()
    {
        AuthorUserId = string.Empty;
        Body = string.Empty;
    }

    public Comment(Guid id, Guid tenantId, Guid proposalId, Guid? requirementId, string authorUserId, string body, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorUserId);
        if (tenantId == Guid.Empty || proposalId == Guid.Empty)
        {
            throw new DomainException("Tenant and proposal are required.");
        }

        Id = id;
        TenantId = tenantId;
        ProposalId = proposalId;
        RequirementId = requirementId;
        AuthorUserId = authorUserId;
        Body = Text.Required(body, MaxBodyLength, "Comment");
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid ProposalId { get; private set; }

    public Guid? RequirementId { get; private set; }

    public string AuthorUserId { get; private set; }

    public string Body { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public string? DeletedByUserId { get; private set; }

    public void Delete(string byUserId, DateTimeOffset at)
    {
        if (DeletedAt is not null)
        {
            return;
        }

        Body = string.Empty;
        DeletedAt = at;
        DeletedByUserId = byUserId;
    }
}

public enum InvitationState
{
    Pending = 0,
    Accepted = 1,
    Revoked = 2,
    Expired = 3,
}

/// <summary>
/// An emailed offer to join a company with a role. Only a hash of the secret token is stored. The
/// invitation can be used once, by a signed-in account with the invited email address, before it expires.
/// </summary>
public sealed class Invitation : ITenantOwned
{
    private Invitation()
    {
        Email = string.Empty;
        NormalizedEmail = string.Empty;
        TokenHash = string.Empty;
        InvitedByUserId = string.Empty;
    }

    public Invitation(Guid id, Guid tenantId, string email, CompanyRole role, string tokenHash, string invitedByUserId, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(invitedByUserId);
        var trimmed = (email ?? string.Empty).Trim();
        var at = trimmed.IndexOf('@', StringComparison.Ordinal);
        if (tenantId == Guid.Empty || trimmed.Length is 0 or > 256 || at <= 0 || at != trimmed.LastIndexOf('@') || at == trimmed.Length - 1 || trimmed.Any(char.IsWhiteSpace))
        {
            throw new DomainException("A valid email address is required.");
        }

        if (!Enum.IsDefined(role))
        {
            throw new DomainException("Unknown role.");
        }

        if (expiresAt <= createdAt)
        {
            throw new DomainException("The invitation must expire after it is created.");
        }

        Id = id;
        TenantId = tenantId;
        Email = trimmed;
        NormalizedEmail = trimmed.ToUpperInvariant();
        Role = role;
        TokenHash = tokenHash;
        InvitedByUserId = invitedByUserId;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Email { get; private set; }

    public string NormalizedEmail { get; private set; }

    public CompanyRole Role { get; private set; }

    public string TokenHash { get; private set; }

    public string InvitedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public string? AcceptedByUserId { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public InvitationState StateAt(DateTimeOffset now) =>
        AcceptedAt is not null ? InvitationState.Accepted
        : RevokedAt is not null ? InvitationState.Revoked
        : now >= ExpiresAt ? InvitationState.Expired
        : InvitationState.Pending;

    public void Accept(string userId, string userEmail, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (StateAt(at) != InvitationState.Pending)
        {
            throw new DomainException("This invitation is no longer valid.");
        }

        if (!string.Equals((userEmail ?? string.Empty).Trim().ToUpperInvariant(), NormalizedEmail, StringComparison.Ordinal))
        {
            throw new DomainException("This invitation was sent to a different email address.");
        }

        AcceptedAt = at;
        AcceptedByUserId = userId;
    }

    public void Revoke(DateTimeOffset at)
    {
        if (StateAt(at) == InvitationState.Pending)
        {
            RevokedAt = at;
        }
    }
}

/// <summary>Records that an opportunity was already included in an alert for a saved search (deduplication).</summary>
public sealed class SavedSearchAlert : ITenantOwned
{
    private SavedSearchAlert()
    {
    }

    public SavedSearchAlert(Guid id, Guid tenantId, Guid savedSearchId, Guid opportunityId, DateTimeOffset sentAt)
    {
        Id = id;
        TenantId = tenantId;
        SavedSearchId = savedSearchId;
        OpportunityId = opportunityId;
        SentAt = sentAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid SavedSearchId { get; private set; }

    public Guid OpportunityId { get; private set; }

    public DateTimeOffset SentAt { get; private set; }
}

internal static class Text
{
    public static string Required(string? value, int max, string name) =>
        Optional(value, max, name) ?? throw new DomainException($"{name} is required.");

    public static string? Optional(string? value, int max, string name)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return trimmed is { Length: var length } && length > max ? throw new DomainException($"{name} must be at most {max} characters.") : trimmed;
    }
}
