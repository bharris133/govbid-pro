using GovBidPro.Application.Collaboration;
using GovBidPro.Web.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovBidPro.Web.Controllers;

public sealed record EmailPreferenceCommand(bool EmailNotifications);

[ApiController]
[Route("api/v1/notifications")]
[Authorize(Policy = Policies.TenantMember)]
public sealed class NotificationsController(NotificationService notifications) : ControllerBase
{
    /// <summary>The signed-in member's own notifications, newest first, with the unread count.</summary>
    [HttpGet]
    public Task<NotificationListDto> List([FromQuery] bool unreadOnly, CancellationToken cancellationToken) => notifications.ListMineAsync(unreadOnly, cancellationToken);

    [HttpGet("unread-count")]
    public async Task<object> UnreadCount(CancellationToken cancellationToken) => new { unread = await notifications.UnreadCountAsync(cancellationToken) };

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        await notifications.MarkReadAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<object> MarkAllRead(CancellationToken cancellationToken) => new { marked = await notifications.MarkAllReadAsync(cancellationToken) };

    [HttpGet("preferences")]
    public Task<NotificationPreferencesDto> Preferences(CancellationToken cancellationToken) => notifications.GetPreferencesAsync(cancellationToken);

    [HttpPut("preferences")]
    public Task<NotificationPreferencesDto> SetPreferences(EmailPreferenceCommand command, CancellationToken cancellationToken) => notifications.SetEmailPreferenceAsync(command.EmailNotifications, cancellationToken);
}

[ApiController]
[Route("api/v1/tasks")]
[Authorize(Policy = Policies.TenantMember)]
public sealed class TasksController(TaskService tasks) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<TaskDto>> List([FromQuery] bool mine, [FromQuery] Guid? proposalId, [FromQuery] bool includeClosed, CancellationToken cancellationToken) =>
        tasks.ListAsync(new TaskFilter(mine, proposalId, includeClosed), cancellationToken);

    [HttpPost]
    public async Task<ActionResult<TaskDto>> Create(TaskCommand command, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await tasks.CreateAsync(command, cancellationToken));

    [HttpPut("{id:guid}")]
    public Task<TaskDto> Update(Guid id, TaskCommand command, CancellationToken cancellationToken) => tasks.UpdateAsync(id, command, cancellationToken);

    /// <summary>The assignee or a proposal writer and above can change the status.</summary>
    [HttpPost("{id:guid}/status")]
    public Task<TaskDto> SetStatus(Guid id, TaskStatusCommand command, CancellationToken cancellationToken) => tasks.SetStatusAsync(id, command, cancellationToken);
}

[ApiController]
[Route("api/v1/proposals/{proposalId:guid}/comments")]
[Authorize(Policy = Policies.TenantMember)]
public sealed class CommentsController(CommentService comments) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<CommentDto>> List(Guid proposalId, CancellationToken cancellationToken) => comments.ListAsync(proposalId, cancellationToken);

    [HttpPost]
    public async Task<ActionResult<IReadOnlyList<CommentDto>>> Add(Guid proposalId, CommentCommand command, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await comments.AddAsync(proposalId, command, cancellationToken));

    /// <summary>The author or a team manager and above. The comment keeps its place with the text removed.</summary>
    [HttpDelete("{commentId:guid}")]
    public async Task<IActionResult> Delete(Guid proposalId, Guid commentId, CancellationToken cancellationToken)
    {
        await comments.DeleteAsync(proposalId, commentId, cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/company/invitations")]
[Authorize(Policy = Policies.TenantMember)]
public sealed class InvitationsController(InvitationService invitations) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<InvitationDto>> List(CancellationToken cancellationToken) => invitations.ListAsync(cancellationToken);

    /// <summary>Creates an invitation. The accept link is returned once and never stored.</summary>
    [HttpPost]
    public async Task<ActionResult<InvitationCreatedDto>> Invite(InviteCommand command, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return StatusCode(StatusCodes.Status201Created, await invitations.InviteAsync(command, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        await invitations.RevokeAsync(id, cancellationToken);
        return NoContent();
    }
}
