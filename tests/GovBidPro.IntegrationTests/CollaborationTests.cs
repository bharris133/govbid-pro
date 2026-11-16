using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Auditing;
using GovBidPro.Application.Collaboration;
using GovBidPro.Application.Common;
using GovBidPro.Application.Companies;
using GovBidPro.Application.Proposals;
using GovBidPro.Application.SavedSearches;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using GovBidPro.Testing;
using Microsoft.EntityFrameworkCore;

namespace GovBidPro.IntegrationTests;

/// <summary>P1.6: invitations, member administration, notifications, tasks, comments and saved-search alerts.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class CollaborationTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private async Task<(TestWorld World, Guid Tenant, string Admin, TenantContextHolder AdminCtx)> SetupAsync()
    {
        var world = new TestWorld(await postgres.CreateMigratedDatabaseAsync(), new MutableTimeProvider(Start));
        var (tenant, admin) = await world.CreateCompanyAsync("Collab Co");
        return (world, tenant, admin, TestWorld.Tenant(tenant, admin));
    }

    private static string TokenOf(InvitationCreatedDto created) => created.AcceptPath.Split("token=")[1];

    [Fact]
    public async Task Invitation_is_emailed_single_use_bound_to_the_email_and_expires()
    {
        var (world, tenant, admin, adminCtx) = await SetupAsync();
        var invitee = await world.CreateUserAsync("new.writer@example.test");
        var stranger = await world.CreateUserAsync("someone.else@example.test");
        InvitationCreatedDto created;
        await using (var db = world.Context(adminCtx))
        {
            var audit = new RecordingAuditLog();
            var service = world.Invitations(db, adminCtx, audit);
            await Assert.ThrowsAsync<RequestValidationException>(() => service.InviteAsync(new InviteCommand("not-an-email", CompanyRole.Reviewer), CancellationToken.None));
            created = await service.InviteAsync(new InviteCommand(" New.Writer@Example.test ", CompanyRole.ProposalWriter), CancellationToken.None);
            Assert.True(created.EmailSent);
            Assert.Equal(Start.AddDays(7), created.Invitation.ExpiresAt);
            var mail = Assert.Single(world.Email.Sent);
            Assert.Equal("New.Writer@Example.test", mail.To);
            Assert.Contains("https://app.example.test/Invitations/Accept?token=" + TokenOf(created), mail.TextBody, StringComparison.Ordinal);
            Assert.Contains(audit.Entries, e => e.Action == AuditActions.InvitationCreated);

            // Only a hash is stored.
            var stored = await db.Invitations.AsNoTracking().SingleAsync();
            Assert.NotEqual(TokenOf(created), stored.TokenHash);
            Assert.Equal(InvitationService.HashToken(TokenOf(created)), stored.TokenHash);
        }

        await using (var db = world.Context(PostgresFixture.SystemContext()))
        {
            var anonymous = new TenantContextHolder();
            var service = world.Invitations(db, anonymous);
            Assert.Null(await service.PreviewAsync("wrong-token", CancellationToken.None));
            var preview = await service.PreviewAsync(TokenOf(created), CancellationToken.None);
            Assert.Equal("Collab Co", preview!.CompanyName);
            Assert.Equal(InvitationState.Pending, preview.State);

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.AcceptAsync(invitee, "wrong-token", CancellationToken.None));
            var mismatch = await Assert.ThrowsAsync<PolicyDeniedException>(() => service.AcceptAsync(stranger, TokenOf(created), CancellationToken.None));
            Assert.Equal("invitation.email_mismatch", mismatch.ReasonCode);

            Assert.Equal(tenant, await service.AcceptAsync(invitee, TokenOf(created), CancellationToken.None));
            await Assert.ThrowsAsync<ConflictException>(() => service.AcceptAsync(invitee, TokenOf(created), CancellationToken.None));
            var membership = await db.CompanyMemberships.AsNoTracking().SingleAsync(m => m.UserId == invitee);
            Assert.Equal(CompanyRole.ProposalWriter, membership.Role);
            Assert.True(membership.IsActive);
            Assert.True(await db.AuditEvents.AnyAsync(e => e.Action == AuditActions.InvitationAccepted && e.Outcome == AuditOutcome.Denied && e.ReasonCode == "invitation.email_mismatch"));
        }

        // One company per account: someone who already belongs to a company cannot accept.
        var (_, elsewhere) = await world.CreateCompanyAsync("Elsewhere Co");
        await using (var db = world.Context(adminCtx))
        {
            var email = await db.Users.Where(u => u.Id == elsewhere).Select(u => u.Email!).SingleAsync();
            var offer = await world.Invitations(db, adminCtx).InviteAsync(new InviteCommand(email, CompanyRole.Reviewer), CancellationToken.None);
            var taken = await Assert.ThrowsAsync<ConflictException>(() => world.Invitations(db, adminCtx).AcceptAsync(elsewhere, TokenOf(offer), CancellationToken.None));
            Assert.Contains("one company at a time", taken.Message, StringComparison.Ordinal);
            Assert.NotNull(admin);
        }

        await using (var db = world.Context(adminCtx))
        {
            // The inviter is told, and a member cannot be invited again.
            Assert.Contains((await world.Notifications(db, adminCtx).ListMineAsync(false, CancellationToken.None)).Items, n => n.Kind == NotificationKinds.MemberJoined);
            await Assert.ThrowsAsync<ConflictException>(() => world.Invitations(db, adminCtx).InviteAsync(new InviteCommand("new.writer@example.test", CompanyRole.Reviewer), CancellationToken.None));
        }
    }

    [Fact]
    public async Task Expired_revoked_and_replaced_invitations_do_not_work_and_only_admins_invite()
    {
        var (world, tenant, _, adminCtx) = await SetupAsync();
        var first = await world.CreateUserAsync("first@example.test");
        var second = await world.CreateUserAsync("second@example.test", confirmed: false);
        var managerId = await world.AddMemberAsync(tenant, CompanyRole.TeamManager);
        var manager = TestWorld.Tenant(tenant, managerId, CompanyRole.TeamManager);
        await using (var db = world.Context(manager))
        {
            await Assert.ThrowsAsync<PolicyDeniedException>(() => world.Invitations(db, manager).InviteAsync(new InviteCommand("x@example.test", CompanyRole.Reviewer), CancellationToken.None));
            await Assert.ThrowsAsync<PolicyDeniedException>(() => world.Invitations(db, manager).ListAsync(CancellationToken.None));
        }

        string replaced, current, toRevoke, unconfirmed;
        await using (var db = world.Context(adminCtx))
        {
            var service = world.Invitations(db, adminCtx);
            world.Email.Fail = true;
            var failed = await service.InviteAsync(new InviteCommand("first@example.test", CompanyRole.Reviewer), CancellationToken.None);
            Assert.False(failed.EmailSent);
            replaced = TokenOf(failed);
            world.Email.Fail = false;
            current = TokenOf(await service.InviteAsync(new InviteCommand("FIRST@example.test", CompanyRole.TeamManager), CancellationToken.None));
            var revocable = await service.InviteAsync(new InviteCommand("third@example.test", CompanyRole.Reviewer), CancellationToken.None);
            toRevoke = TokenOf(revocable);
            await service.RevokeAsync(revocable.Invitation.Id, CancellationToken.None);
            await Assert.ThrowsAsync<ConflictException>(() => service.RevokeAsync(revocable.Invitation.Id, CancellationToken.None));
            unconfirmed = TokenOf(await service.InviteAsync(new InviteCommand("second@example.test", CompanyRole.Reviewer), CancellationToken.None));
            var states = (await service.ListAsync(CancellationToken.None)).ToLookup(i => i.State);
            Assert.Equal(2, states[InvitationState.Pending].Count());
            Assert.Equal(2, states[InvitationState.Revoked].Count());
        }

        world.Collaboration = new CollaborationOptions { PublicBaseUrl = "https://app.example.test", RequireConfirmedEmailToAcceptInvitation = true };
        await using (var db = world.Context(PostgresFixture.SystemContext()))
        {
            var service = world.Invitations(db, new TenantContextHolder());
            await Assert.ThrowsAsync<ConflictException>(() => service.AcceptAsync(first, replaced, CancellationToken.None));
            Assert.Equal(InvitationState.Revoked, (await service.PreviewAsync(toRevoke, CancellationToken.None))!.State);
            var denied = await Assert.ThrowsAsync<PolicyDeniedException>(() => service.AcceptAsync(second, unconfirmed, CancellationToken.None));
            Assert.Equal("invitation.email_not_confirmed", denied.ReasonCode);

            world.Clock.Advance(TimeSpan.FromDays(7));
            Assert.Equal(InvitationState.Expired, (await service.PreviewAsync(current, CancellationToken.None))!.State);
            await Assert.ThrowsAsync<ConflictException>(() => service.AcceptAsync(first, current, CancellationToken.None));
            Assert.False(await db.CompanyMemberships.AnyAsync(m => m.UserId == first));
        }
    }

    [Fact]
    public async Task Member_administration_keeps_one_admin_and_removal_cuts_access()
    {
        var (world, tenant, admin, adminCtx) = await SetupAsync();
        var writerId = await world.AddMemberAsync(tenant, CompanyRole.ProposalWriter);
        var reviewerId = await world.AddMemberAsync(tenant, CompanyRole.Reviewer);
        await world.ImportAsync(world.Notice("C1"));
        var opportunity = await world.OpportunityIdAsync("C1");
        var (otherTenant, otherAdmin) = await world.CreateCompanyAsync("Other Co");

        var writer = TestWorld.Tenant(tenant, writerId, CompanyRole.ProposalWriter);
        await using (var db = world.Context(writer))
        {
            await Assert.ThrowsAsync<PolicyDeniedException>(() => world.Members(db, writer).RemoveAsync(reviewerId, CancellationToken.None));
            await Assert.ThrowsAsync<PolicyDeniedException>(() => world.Members(db, writer).ChangeRoleAsync(writerId, new ChangeRoleCommand(CompanyRole.CompanyAdmin), CancellationToken.None));
        }

        await using (var db = world.Context(adminCtx))
        {
            var members = world.Members(db, adminCtx);
            await Assert.ThrowsAsync<ConflictException>(() => members.RemoveAsync(admin, CancellationToken.None));
            await Assert.ThrowsAsync<ConflictException>(() => members.ChangeRoleAsync(admin, new ChangeRoleCommand(CompanyRole.TeamManager), CancellationToken.None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => members.RemoveAsync(otherAdmin, CancellationToken.None));

            Assert.Equal(CompanyRole.TeamManager, (await members.ChangeRoleAsync(writerId, new ChangeRoleCommand(CompanyRole.TeamManager), CancellationToken.None)).Role);

            // A pending review for a removed reviewer is superseded so approval is not blocked forever.
            var proposals = world.Proposals(db, adminCtx);
            var proposal = await proposals.CreateAsync(new CreateProposalCommand(opportunity, null, null), CancellationToken.None);
            await proposals.ChangeStatusAsync(proposal.Id, new ChangeStatusCommand(ProposalStatus.InProgress, null, null), CancellationToken.None);
            await proposals.ChangeStatusAsync(proposal.Id, new ChangeStatusCommand(ProposalStatus.InReview, null, null), CancellationToken.None);
            await proposals.RequestReviewAsync(proposal.Id, new RequestReviewCommand(reviewerId, null), CancellationToken.None);

            var audit = new RecordingAuditLog();
            await world.Members(db, adminCtx, audit).RemoveAsync(reviewerId, CancellationToken.None);
            Assert.Contains(audit.Entries, e => e.Action == AuditActions.MemberRemoved);
            Assert.Equal(2, (await members.ListActiveAsync(CancellationToken.None)).Count);
            Assert.Equal(ReviewOutcome.Superseded, Assert.Single((await proposals.GetAsync(proposal.Id, CancellationToken.None)).Reviews).Outcome);
            await Assert.ThrowsAsync<RequestValidationException>(() => proposals.RequestReviewAsync(proposal.Id, new RequestReviewCommand(reviewerId, null), CancellationToken.None));
        }

        await using (var db = world.Context(PostgresFixture.SystemContext()))
        {
            // Tenant resolution no longer finds a company for the removed user.
            Assert.Empty(await new CompanyService(db, world.Clock).GetActiveMembershipsAsync(reviewerId, CancellationToken.None));
            Assert.NotEqual(tenant, otherTenant);
        }

        // Invited back: the same membership row is reactivated with the new role.
        await using (var db = world.Context(adminCtx))
        {
            var email = await db.Users.Where(u => u.Id == reviewerId).Select(u => u.Email!).SingleAsync();
            var created = await world.Invitations(db, adminCtx).InviteAsync(new InviteCommand(email, CompanyRole.ProposalWriter), CancellationToken.None);
            await world.Invitations(db, adminCtx).AcceptAsync(reviewerId, TokenOf(created), CancellationToken.None);
            var membership = await db.CompanyMemberships.AsNoTracking().SingleAsync(m => m.UserId == reviewerId);
            Assert.True(membership.IsActive);
            Assert.Equal(CompanyRole.ProposalWriter, membership.Role);
        }
    }

    [Fact]
    public async Task Tasks_notify_the_assignee_who_can_complete_them_whatever_their_role()
    {
        var (world, tenant, _, adminCtx) = await SetupAsync();
        var reviewerId = await world.AddMemberAsync(tenant, CompanyRole.Reviewer, "reviewer@example.test");
        var otherReviewerId = await world.AddMemberAsync(tenant, CompanyRole.Reviewer);
        var (_, outsider) = await world.CreateCompanyAsync("Other Co");
        Guid taskId;
        await using (var db = world.Context(adminCtx))
        {
            var tasks = world.Tasks(db, adminCtx);
            await Assert.ThrowsAsync<RequestValidationException>(() => tasks.CreateAsync(new TaskCommand("Draft", null, outsider, null), CancellationToken.None));
            await Assert.ThrowsAsync<RequestValidationException>(() => tasks.CreateAsync(new TaskCommand(" ", null, null, null), CancellationToken.None));
            await Assert.ThrowsAsync<RequestValidationException>(() => tasks.CreateAsync(new TaskCommand("Draft", null, null, null, Guid.NewGuid()), CancellationToken.None));
            var task = await tasks.CreateAsync(new TaskCommand("Review past performance", "Check the three references", reviewerId, new DateOnly(2026, 11, 20)), CancellationToken.None);
            taskId = task.Id;
            Assert.Equal("reviewer@example.test", task.Assignee);
            Assert.False(task.Overdue);
            await tasks.CreateAsync(new TaskCommand("Unassigned chore", null, null, null), CancellationToken.None);
            Assert.Equal(2, (await tasks.ListAsync(new TaskFilter(), CancellationToken.None)).Count);
            Assert.Empty(await tasks.ListAsync(new TaskFilter(MineOnly: true), CancellationToken.None));
        }

        var mail = Assert.Single(world.Email.Sent);
        Assert.Equal("reviewer@example.test", mail.To);
        Assert.Contains("https://app.example.test/Tasks", mail.TextBody, StringComparison.Ordinal);

        var other = TestWorld.Tenant(tenant, otherReviewerId, CompanyRole.Reviewer);
        await using (var db = world.Context(other))
        {
            await Assert.ThrowsAsync<PolicyDeniedException>(() => world.Tasks(db, other).SetStatusAsync(taskId, new TaskStatusCommand(WorkTaskStatus.Done), CancellationToken.None));
            await Assert.ThrowsAsync<PolicyDeniedException>(() => world.Tasks(db, other).CreateAsync(new TaskCommand("x", null, null, null), CancellationToken.None));
        }

        world.Clock.Advance(TimeSpan.FromDays(5));
        var reviewer = TestWorld.Tenant(tenant, reviewerId, CompanyRole.Reviewer);
        await using (var db = world.Context(reviewer))
        {
            var tasks = world.Tasks(db, reviewer);
            var mine = Assert.Single(await tasks.ListAsync(new TaskFilter(MineOnly: true), CancellationToken.None));
            Assert.True(mine.Overdue);
            var notifications = world.Notifications(db, reviewer);
            var list = await notifications.ListMineAsync(false, CancellationToken.None);
            Assert.Equal(1, list.Unread);
            Assert.Equal(NotificationKinds.TaskAssigned, list.Items[0].Kind);
            await notifications.MarkReadAsync(list.Items[0].Id, CancellationToken.None);
            Assert.Equal(0, await notifications.UnreadCountAsync(CancellationToken.None));

            Assert.Equal(WorkTaskStatus.Done, (await tasks.SetStatusAsync(taskId, new TaskStatusCommand(WorkTaskStatus.Done), CancellationToken.None)).Status);
            Assert.Empty(await tasks.ListAsync(new TaskFilter(MineOnly: true), CancellationToken.None));
            Assert.Single(await tasks.ListAsync(new TaskFilter(MineOnly: true, IncludeClosed: true), CancellationToken.None));

            // Opting out stops email, not in-app notifications.
            Assert.False((await notifications.SetEmailPreferenceAsync(false, CancellationToken.None)).EmailNotifications);
        }

        await using (var db = world.Context(adminCtx))
        {
            await world.Tasks(db, adminCtx).CreateAsync(new TaskCommand("Second task", null, reviewerId, null), CancellationToken.None);
        }

        Assert.Single(world.Email.Sent);
        await using (var db = world.Context(reviewer))
        {
            Assert.Equal(1, await world.Notifications(db, reviewer).UnreadCountAsync(CancellationToken.None));
        }

        var outsiderCtx = TestWorld.Tenant((await OtherTenantAsync(world, outsider)), outsider);
        await using (var db = world.Context(outsiderCtx))
        {
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => world.Tasks(db, outsiderCtx).SetStatusAsync(taskId, new TaskStatusCommand(WorkTaskStatus.Cancelled), CancellationToken.None));
            Assert.Empty(await world.Tasks(db, outsiderCtx).ListAsync(new TaskFilter(IncludeClosed: true), CancellationToken.None));
            Assert.Empty((await world.Notifications(db, outsiderCtx).ListMineAsync(false, CancellationToken.None)).Items);
        }
    }

    [Fact]
    public async Task Comments_are_open_to_all_members_moderated_and_tenant_scoped()
    {
        var (world, tenant, admin, adminCtx) = await SetupAsync();
        var reviewerId = await world.AddMemberAsync(tenant, CompanyRole.Reviewer);
        var writerId = await world.AddMemberAsync(tenant, CompanyRole.ProposalWriter);
        await world.ImportAsync(world.Notice("C2"));
        var opportunity = await world.OpportunityIdAsync("C2");
        Guid proposalId;
        Guid requirementId;
        await using (var db = world.Context(adminCtx))
        {
            var proposals = world.Proposals(db, adminCtx);
            proposalId = (await proposals.CreateAsync(new CreateProposalCommand(opportunity, null, null), CancellationToken.None)).Id;
            requirementId = (await proposals.AddRequirementAsync(proposalId, new RequirementCommand("L.1", "Staffing plan", null, null, true, null, null, writerId, null), CancellationToken.None)).Id;
        }

        var reviewer = TestWorld.Tenant(tenant, reviewerId, CompanyRole.Reviewer);
        Guid commentId;
        await using (var db = world.Context(reviewer))
        {
            var comments = world.Comments(db, reviewer);
            await Assert.ThrowsAsync<RequestValidationException>(() => comments.AddAsync(proposalId, new CommentCommand("  "), CancellationToken.None));
            await Assert.ThrowsAsync<RequestValidationException>(() => comments.AddAsync(proposalId, new CommentCommand("x", Guid.NewGuid()), CancellationToken.None));
            var list = await comments.AddAsync(proposalId, new CommentCommand("Pricing volume is missing the travel estimate.", requirementId), CancellationToken.None);
            var comment = Assert.Single(list);
            commentId = comment.Id;
            Assert.Equal("L.1", comment.RequirementReference);
            Assert.True(comment.CanDelete);
        }

        // Owner and requirement owner are notified; the comment text is not put in the email.
        Assert.Equal(2, world.Email.Sent.Count);
        Assert.DoesNotContain(world.Email.Sent, m => m.TextBody.Contains("travel estimate", StringComparison.Ordinal));

        var writer = TestWorld.Tenant(tenant, writerId, CompanyRole.ProposalWriter);
        await using (var db = world.Context(writer))
        {
            var comments = world.Comments(db, writer);
            Assert.False(Assert.Single(await comments.ListAsync(proposalId, CancellationToken.None)).CanDelete);
            await Assert.ThrowsAsync<PolicyDeniedException>(() => comments.DeleteAsync(proposalId, commentId, CancellationToken.None));
        }

        await using (var db = world.Context(adminCtx))
        {
            var comments = world.Comments(db, adminCtx);
            await comments.DeleteAsync(proposalId, commentId, CancellationToken.None);
            var deleted = Assert.Single(await comments.ListAsync(proposalId, CancellationToken.None));
            Assert.True(deleted.Deleted);
            Assert.Equal(string.Empty, deleted.Body);
            Assert.Equal(admin, (await db.Comments.AsNoTracking().SingleAsync()).DeletedByUserId);
        }

        var (otherTenant, otherAdmin) = await world.CreateCompanyAsync("Other Co");
        var other = TestWorld.Tenant(otherTenant, otherAdmin);
        await using (var db = world.Context(other))
        {
            var comments = world.Comments(db, other);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => comments.ListAsync(proposalId, CancellationToken.None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => comments.AddAsync(proposalId, new CommentCommand("hello"), CancellationToken.None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => comments.DeleteAsync(proposalId, commentId, CancellationToken.None));
        }
    }

    [Fact]
    public async Task Proposal_assignments_and_reviews_reach_people_through_notifications()
    {
        var (world, tenant, admin, adminCtx) = await SetupAsync();
        var reviewerId = await world.AddMemberAsync(tenant, CompanyRole.Reviewer);
        await world.ImportAsync(world.Notice("C3"));
        var opportunity = await world.OpportunityIdAsync("C3");
        Guid proposalId;
        await using (var db = world.Context(adminCtx))
        {
            var proposals = world.Proposals(db, adminCtx, notifier: new ProposalNotifier(world.Notifications(db, adminCtx), adminCtx));
            var proposal = await proposals.CreateAsync(new CreateProposalCommand(opportunity, "Bid", null), CancellationToken.None);
            proposalId = proposal.Id;

            // Assigning yourself does not notify you.
            await proposals.UpdateSectionAsync(proposalId, proposal.Sections[0].Id, new SectionCommand(proposal.Sections[0].Title, admin, false), CancellationToken.None);
            Assert.Empty(world.Email.Sent);
            await proposals.ChangeStatusAsync(proposalId, new ChangeStatusCommand(ProposalStatus.InProgress, null, null), CancellationToken.None);
            await proposals.ChangeStatusAsync(proposalId, new ChangeStatusCommand(ProposalStatus.InReview, null, null), CancellationToken.None);
            await proposals.RequestReviewAsync(proposalId, new RequestReviewCommand(reviewerId, "Check compliance"), CancellationToken.None);
        }

        var reviewer = TestWorld.Tenant(tenant, reviewerId, CompanyRole.Reviewer);
        await using (var db = world.Context(reviewer))
        {
            var request = Assert.Single((await world.Notifications(db, reviewer).ListMineAsync(true, CancellationToken.None)).Items);
            Assert.Equal(NotificationKinds.ReviewRequested, request.Kind);
            Assert.Equal($"/Proposals/Detail?id={proposalId}", request.Link);
            var reviewId = await db.ProposalReviews.Select(r => r.Id).SingleAsync();
            await world.Proposals(db, reviewer, notifier: new ProposalNotifier(world.Notifications(db, reviewer), reviewer)).DecideReviewAsync(proposalId, reviewId, new DecideReviewCommand(ReviewOutcome.Approved, "Good to go"), CancellationToken.None);
        }

        await using (var db = world.Context(adminCtx))
        {
            var decided = Assert.Single((await world.Notifications(db, adminCtx).ListMineAsync(true, CancellationToken.None)).Items);
            Assert.Equal(NotificationKinds.ReviewDecided, decided.Kind);
            Assert.Contains("approved", decided.Title, StringComparison.Ordinal);
            Assert.Equal(1, await world.Notifications(db, adminCtx).MarkAllReadAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task Saved_search_alerts_follow_the_frequency_and_never_repeat_a_notice()
    {
        var (world, tenant, admin, adminCtx) = await SetupAsync();
        await world.ImportAsync(world.Notice("OLD", "Request for Proposal: Cloud hosting (existing)"));
        Guid daily;
        await using (var db = world.Context(adminCtx))
        {
            var searches = world.SavedSearches(db, adminCtx);
            daily = (await searches.CreateAsync(new CreateSavedSearchCommand("Cloud work", new OpportunitySearchCriteria { Keywords = "cloud" }, NotificationFrequency.Daily), CancellationToken.None)).Id;
            await searches.CreateAsync(new CreateSavedSearchCommand("Cloud weekly", new OpportunitySearchCriteria { Keywords = "cloud" }, NotificationFrequency.Weekly), CancellationToken.None);
            await searches.CreateAsync(new CreateSavedSearchCommand("No alerts", new OpportunitySearchCriteria { Keywords = "cloud" }, NotificationFrequency.None), CancellationToken.None);
        }

        var system = PostgresFixture.SystemContext();
        async Task<AlertRunSummary> RunAsync()
        {
            await using var db = world.Context(system);
            return await world.Alerts(db, system).RunAsync(CancellationToken.None);
        }

        // First run: both alerting searches are checked; the notice that predates them is not reported.
        world.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(new AlertRunSummary(2, 0, 0), await RunAsync());

        world.Clock.Advance(TimeSpan.FromHours(2));
        await world.ImportAsync(world.Notice("NEW1", "Request for Proposal: Cloud migration"), world.Notice("NEW2", "Request for Quotation: Cloud backup"), world.Notice("OTHER", "Request for Proposal: Janitorial"));

        // Not due yet (less than a day since the last check).
        Assert.Equal(new AlertRunSummary(0, 0, 0), await RunAsync());

        world.Clock.Advance(TimeSpan.FromHours(23));
        Assert.Equal(new AlertRunSummary(1, 1, 2), await RunAsync());
        var mail = Assert.Single(world.Email.Sent);
        Assert.Contains("2 new opportunities match “Cloud work”", mail.Subject, StringComparison.Ordinal);
        Assert.Contains($"https://app.example.test/Opportunities?savedSearch={daily}", mail.TextBody, StringComparison.Ordinal);

        // The next day nothing new arrived: no repeat.
        world.Clock.Advance(TimeSpan.FromHours(25));
        Assert.Equal(new AlertRunSummary(1, 0, 0), await RunAsync());

        // The weekly search reports the same notices once, a week after its last check.
        world.Clock.Advance(TimeSpan.FromDays(5));
        var weekly = await RunAsync();
        Assert.Equal(2, weekly.SearchesChecked);
        Assert.Equal(1, weekly.AlertsSent);
        Assert.Equal(2, weekly.OpportunitiesReported);

        await using (var db = world.Context(adminCtx))
        {
            var alerts = (await world.Notifications(db, adminCtx).ListMineAsync(false, CancellationToken.None)).Items.Where(n => n.Kind == NotificationKinds.SavedSearchAlert).ToList();
            Assert.Equal(2, alerts.Count);
            Assert.Equal(4, await db.SavedSearchAlerts.CountAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => world.Alerts(db, adminCtx).RunAsync(CancellationToken.None));
        }

        Assert.NotEqual(Guid.Empty, tenant);
        Assert.NotNull(admin);
    }

    private static async Task<Guid> OtherTenantAsync(TestWorld world, string userId)
    {
        await using var db = world.Context(PostgresFixture.SystemContext());
        return await db.CompanyMemberships.Where(m => m.UserId == userId).Select(m => m.TenantId).SingleAsync();
    }
}
