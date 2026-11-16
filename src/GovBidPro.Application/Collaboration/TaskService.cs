using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Auditing;
using GovBidPro.Application.Common;
using GovBidPro.Application.Companies;
using GovBidPro.Domain.Common;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GovBidPro.Application.Collaboration;

public sealed record TaskCommand(string Title, string? Description, string? AssigneeUserId, DateOnly? DueOn, Guid? ProposalId = null);

public sealed record TaskStatusCommand(WorkTaskStatus Status);

public sealed record TaskFilter(bool MineOnly = false, Guid? ProposalId = null, bool IncludeClosed = false);

public sealed record TaskDto(Guid Id, string Title, string? Description, string? AssigneeUserId, string? Assignee, DateOnly? DueOn, WorkTaskStatus Status, Guid? ProposalId, string? ProposalTitle, string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool Overdue);

/// <summary>
/// Tasks for members, optionally tied to a proposal. Writers and above create and edit; the assignee
/// (any role) can always move their own task forward.
/// </summary>
public sealed class TaskService(IAppDbContext db, ITenantContext tenant, IAuditLog audit, IUserDirectory users, NotificationService notifications, TimeProvider clock)
{
    public async Task<IReadOnlyList<TaskDto>> ListAsync(TaskFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var tenantId = tenant.RequireTenant();
        var userId = tenant.RequireUser();
        var query = db.WorkTasks.AsNoTracking().Where(t => t.TenantId == tenantId);
        if (filter.MineOnly)
        {
            query = query.Where(t => t.AssigneeUserId == userId);
        }

        if (filter.ProposalId is { } proposalId)
        {
            query = query.Where(t => t.ProposalId == proposalId);
        }

        if (!filter.IncludeClosed)
        {
            query = query.Where(t => t.Status == WorkTaskStatus.Open || t.Status == WorkTaskStatus.InProgress);
        }

        var rows = await query.OrderBy(t => t.DueOn == null).ThenBy(t => t.DueOn).ThenByDescending(t => t.CreatedAt).Take(500).ToListAsync(cancellationToken);
        return await ToDtosAsync(rows, cancellationToken);
    }

    public async Task<TaskDto> CreateAsync(TaskCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        tenant.RequireRole(CompanyRole.ProposalWriter);
        var tenantId = tenant.TenantId;
        var userId = tenant.RequireUser();
        await ValidateLinksAsync(tenantId, command, cancellationToken);
        var task = Mutate(() => new WorkTask(Guid.NewGuid(), tenantId, command.ProposalId, command.Title, command.Description, command.AssigneeUserId, command.DueOn, userId, clock.GetUtcNow()));
        db.WorkTasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.TaskChanged, "task", task.Id.ToString(), AuditOutcome.Success, "created", cancellationToken);
        await NotifyAssigneeAsync(task, userId, cancellationToken);
        return (await ToDtosAsync([task], cancellationToken))[0];
    }

    public async Task<TaskDto> UpdateAsync(Guid id, TaskCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        tenant.RequireRole(CompanyRole.ProposalWriter);
        var userId = tenant.RequireUser();
        var task = await FindAsync(id, cancellationToken);
        await ValidateLinksAsync(task.TenantId, command with { ProposalId = null }, cancellationToken);
        var previous = task.AssigneeUserId;
        Mutate(() => task.Update(command.Title, command.Description, command.AssigneeUserId, command.DueOn, clock.GetUtcNow()));
        await SaveAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.TaskChanged, "task", id.ToString(), AuditOutcome.Success, "updated", cancellationToken);
        if (!string.Equals(previous, task.AssigneeUserId, StringComparison.Ordinal))
        {
            await NotifyAssigneeAsync(task, userId, cancellationToken);
        }

        return (await ToDtosAsync([task], cancellationToken))[0];
    }

    public async Task<TaskDto> SetStatusAsync(Guid id, TaskStatusCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var userId = tenant.RequireUser();
        var task = await FindAsync(id, cancellationToken);
        if (!string.Equals(task.AssigneeUserId, userId, StringComparison.Ordinal))
        {
            tenant.RequireRole(CompanyRole.ProposalWriter);
        }

        Mutate(() => task.SetStatus(command.Status, clock.GetUtcNow()));
        await SaveAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.TaskChanged, "task", id.ToString(), AuditOutcome.Success, $"status.{command.Status}", cancellationToken);
        return (await ToDtosAsync([task], cancellationToken))[0];
    }

    private Task NotifyAssigneeAsync(WorkTask task, string actorUserId, CancellationToken cancellationToken) =>
        task.AssigneeUserId is null || task.AssigneeUserId == actorUserId
            ? Task.CompletedTask
            : notifications.PublishAsync(task.TenantId, [task.AssigneeUserId], NotificationKinds.TaskAssigned, $"Task assigned to you: {task.Title}", task.DueOn is { } due ? $"Due {due:yyyy-MM-dd}" : null, "/Tasks", cancellationToken);

    private async Task ValidateLinksAsync(Guid tenantId, TaskCommand command, CancellationToken cancellationToken)
    {
        await db.EnsureActiveMemberAsync(tenantId, string.IsNullOrWhiteSpace(command.AssigneeUserId) ? null : command.AssigneeUserId, "task.assignee_not_member", cancellationToken);
        if (command.ProposalId is { } proposalId && !await db.Proposals.AnyAsync(p => p.Id == proposalId && p.TenantId == tenantId, cancellationToken))
        {
            throw new RequestValidationException("task.proposal_not_found", "That proposal was not found.");
        }
    }

    private async Task<IReadOnlyList<TaskDto>> ToDtosAsync(IReadOnlyList<WorkTask> tasks, CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        var proposalIds = tasks.Where(t => t.ProposalId is not null).Select(t => t.ProposalId!.Value).Distinct().ToList();
        var titles = await db.Proposals.AsNoTracking().Where(p => p.TenantId == tenantId && proposalIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken);
        var names = await users.GetAsync(tasks.SelectMany(t => new[] { t.AssigneeUserId, t.CreatedByUserId }).Where(id => id is not null).Select(id => id!).Distinct().ToList(), cancellationToken);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return tasks.Select(t => new TaskDto(
            t.Id, t.Title, t.Description, t.AssigneeUserId, t.AssigneeUserId is null ? null : names.NameOf(t.AssigneeUserId), t.DueOn, t.Status, t.ProposalId,
            t.ProposalId is { } pid && titles.TryGetValue(pid, out var title) ? title : null, names.NameOf(t.CreatedByUserId), t.CreatedAt, t.UpdatedAt,
            t.IsOpen && t.DueOn is { } due && due < today)).ToList();
    }

    private async Task<WorkTask> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        var task = await db.WorkTasks.SingleOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, cancellationToken);
        if (task is null)
        {
            await audit.RecordAsync(AuditActions.AccessDenied, "task", id.ToString(), AuditOutcome.Denied, "not_found_or_cross_tenant", cancellationToken);
            throw new ResourceNotFoundException();
        }

        return task;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Someone else changed this task at the same time. Reload and try again.");
        }
    }

    private static void Mutate(Action action) => Mutate(() =>
    {
        action();
        return true;
    });

    private static T Mutate<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (DomainException ex)
        {
            throw new RequestValidationException("task.invalid", ex.Message);
        }
    }
}
