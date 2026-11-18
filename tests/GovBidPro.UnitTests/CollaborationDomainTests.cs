using GovBidPro.Application.Collaboration;
using GovBidPro.Domain.Common;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using GovBidPro.Infrastructure.Email;

namespace GovBidPro.UnitTests;

public sealed class CollaborationDomainTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.NewGuid();

    private static Invitation NewInvitation(string email = "Pat@Example.test") => new(Guid.NewGuid(), Tenant, email, CompanyRole.ProposalWriter, "hash", "inviter", At, At.AddDays(7));

    [Theory]
    [InlineData("")]
    [InlineData("no-at-sign")]
    [InlineData("two@@example.test")]
    [InlineData("trailing@")]
    [InlineData("has space@example.test")]
    public void Invitations_need_a_plausible_email(string email) => Assert.Throws<DomainException>(() => NewInvitation(email));

    [Fact]
    public void Invitation_state_follows_time_acceptance_and_revocation()
    {
        var invitation = NewInvitation();
        Assert.Equal("PAT@EXAMPLE.TEST", invitation.NormalizedEmail);
        Assert.Equal(InvitationState.Pending, invitation.StateAt(At.AddDays(6)));
        Assert.Equal(InvitationState.Expired, invitation.StateAt(At.AddDays(7)));
        Assert.Throws<DomainException>(() => invitation.Accept("u", "pat@example.test", At.AddDays(8)));
        Assert.Throws<DomainException>(() => invitation.Accept("u", "other@example.test", At.AddDays(1)));

        invitation.Accept("u", " pat@example.test ", At.AddDays(1));
        Assert.Equal(InvitationState.Accepted, invitation.StateAt(At.AddDays(30)));
        Assert.Throws<DomainException>(() => invitation.Accept("u2", "pat@example.test", At.AddDays(2)));
        invitation.Revoke(At.AddDays(2));
        Assert.Null(invitation.RevokedAt);

        var revoked = NewInvitation();
        revoked.Revoke(At.AddDays(1));
        Assert.Equal(InvitationState.Revoked, revoked.StateAt(At.AddDays(1)));
        Assert.Throws<DomainException>(() => new Invitation(Guid.NewGuid(), Tenant, "a@example.test", CompanyRole.Reviewer, "h", "i", At, At));
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/ok\\..\\x")]
    public void Notification_links_must_stay_inside_the_app(string link) =>
        Assert.Throws<DomainException>(() => new Notification(Guid.NewGuid(), Tenant, "u", "kind", "Title", null, link, At));

    [Fact]
    public void Notifications_are_read_once()
    {
        var notification = new Notification(Guid.NewGuid(), Tenant, "u", "kind", " Title ", " ", "/Tasks", At);
        Assert.Equal("Title", notification.Title);
        Assert.Null(notification.Body);
        notification.MarkRead(At.AddHours(1));
        notification.MarkRead(At.AddHours(2));
        Assert.Equal(At.AddHours(1), notification.ReadAt);
    }

    [Fact]
    public void Tasks_track_completion_and_validate_text()
    {
        var task = new WorkTask(Guid.NewGuid(), Tenant, null, " Draft volume 1 ", null, " ", new DateOnly(2026, 12, 1), "creator", At);
        Assert.Equal("Draft volume 1", task.Title);
        Assert.Null(task.AssigneeUserId);
        Assert.True(task.IsOpen);
        task.SetStatus(WorkTaskStatus.Done, At.AddDays(1));
        Assert.False(task.IsOpen);
        Assert.Equal(At.AddDays(1), task.CompletedAt);
        task.SetStatus(WorkTaskStatus.InProgress, At.AddDays(2));
        Assert.Null(task.CompletedAt);
        Assert.Throws<DomainException>(() => task.Update(new string('t', 201), null, null, null, At));
        Assert.Throws<DomainException>(() => task.SetStatus((WorkTaskStatus)99, At));
    }

    [Fact]
    public void Deleted_comments_lose_their_text_but_keep_the_record()
    {
        var comment = new Comment(Guid.NewGuid(), Tenant, Guid.NewGuid(), null, "author", " Needs pricing detail ", At);
        Assert.Equal("Needs pricing detail", comment.Body);
        comment.Delete("manager", At.AddHours(1));
        comment.Delete("someone-else", At.AddHours(2));
        Assert.Equal(string.Empty, comment.Body);
        Assert.Equal("manager", comment.DeletedByUserId);
        Assert.Throws<DomainException>(() => new Comment(Guid.NewGuid(), Tenant, Guid.NewGuid(), null, "author", new string('c', 4001), At));
    }

    [Fact]
    public void Removed_members_can_be_reactivated_with_a_new_role()
    {
        var membership = new CompanyMembership(Guid.NewGuid(), Tenant, "u", CompanyRole.Reviewer, MembershipStatus.Active, At);
        Assert.True(membership.EmailNotifications);
        membership.Deprovision(At.AddDays(1));
        Assert.False(membership.IsActive);
        Assert.Equal(At.AddDays(1), membership.RemovedAt);
        membership.Reactivate(CompanyRole.ProposalWriter);
        Assert.True(membership.IsActive);
        Assert.Null(membership.RemovedAt);
        Assert.Throws<DomainException>(() => membership.ChangeRole((CompanyRole)42));
    }

    [Fact]
    public void Collaboration_defaults_are_valid_and_links_use_the_public_address()
    {
        var options = new CollaborationOptions();
        Assert.True(CollaborationOptions.IsValid(options));
        Assert.Equal(7, options.InvitationExpiryDays);
        Assert.Equal(CompanyRole.CompanyAdmin, options.MinimumRoleToInvite);
        Assert.Null(options.AbsoluteLink("/Tasks"));
        options.PublicBaseUrl = "https://app.example.test/";
        Assert.Equal("https://app.example.test/Tasks", options.AbsoluteLink("/Tasks"));
        Assert.False(CollaborationOptions.IsValid(new CollaborationOptions { InvitationExpiryDays = 0 }));
        Assert.False(CollaborationOptions.IsValid(new CollaborationOptions { PublicBaseUrl = "https://app.example.test/some/path" }));
        Assert.False(CollaborationOptions.IsValid(new CollaborationOptions { PublicBaseUrl = "not a url" }));
    }

    [Fact]
    public void Smtp_needs_a_host_and_a_from_address()
    {
        Assert.True(EmailOptions.IsValid(new EmailOptions()));
        Assert.False(EmailOptions.IsValid(new EmailOptions { Provider = EmailProviders.Smtp }));
        Assert.False(EmailOptions.IsValid(new EmailOptions { Provider = EmailProviders.Smtp, FromAddress = "bad", Smtp = { Host = "smtp.example.test" } }));
        Assert.True(EmailOptions.IsValid(new EmailOptions { Provider = EmailProviders.Smtp, FromAddress = "no-reply@example.test", Smtp = { Host = "smtp.example.test" } }));
        Assert.False(EmailProviders.IsKnown("SendGrid"));
    }

    [Fact]
    public void Emailed_links_need_https_except_localhost_in_development()
    {
        Assert.True(CollaborationOptions.IsUsableForEmail("https://app.example.test", isLocalEnvironment: false));
        Assert.False(CollaborationOptions.IsUsableForEmail("http://app.example.test", isLocalEnvironment: false));
        Assert.False(CollaborationOptions.IsUsableForEmail("http://localhost:8080", isLocalEnvironment: false));
        Assert.True(CollaborationOptions.IsUsableForEmail("http://localhost:8080", isLocalEnvironment: true));
        Assert.True(CollaborationOptions.IsUsableForEmail("http://127.0.0.1:8080", isLocalEnvironment: true));
        Assert.False(CollaborationOptions.IsUsableForEmail("http://app.example.test", isLocalEnvironment: true));
        Assert.False(CollaborationOptions.IsUsableForEmail(null, isLocalEnvironment: true));
        Assert.False(CollaborationOptions.IsUsableForEmail("", isLocalEnvironment: false));
    }
}
