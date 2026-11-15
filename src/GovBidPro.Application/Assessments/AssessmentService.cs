using System.Text.Json;
using System.Text.Json.Serialization;
using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Auditing;
using GovBidPro.Application.Common;
using GovBidPro.Domain.Common;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GovBidPro.Application.Assessments;

public sealed class AssessmentOptions
{
    public const string SectionName = "Assessments";

    /// <summary>Lowest company role that may record a Go or No-go decision. Scoring and "Watch" need ProposalWriter.</summary>
    public CompanyRole MinimumRoleForDecision { get; set; } = CompanyRole.TeamManager;

    /// <summary>Lowest company role that may edit the company fit profile.</summary>
    public CompanyRole MinimumRoleForProfile { get; set; } = CompanyRole.TeamManager;
}

public sealed record CompanyProfileDto(CompanyFitProfile Fit, DateTimeOffset? UpdatedAt, string? UpdatedBy, bool IsEmpty);

public sealed record RecordAssessmentCommand(GoNoGoDecision Decision, string? Rationale);

public sealed record AssessmentDto(
    Guid Id,
    Guid OpportunityId,
    int Sequence,
    int OpportunityRevisionNumber,
    int? Score,
    string Band,
    string RuleVersion,
    IReadOnlyList<FitCriterionResult> Criteria,
    GoNoGoDecision Decision,
    string? Rationale,
    string CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record AssessmentSummaryDto(Guid OpportunityId, string OpportunityTitle, DateTimeOffset? ResponseDueAt, int? Score, string Band, GoNoGoDecision Decision, DateTimeOffset DecidedAt, bool OpportunityAmendedSince);

/// <summary>
/// Company profile, explainable fit scoring and Go/No-go decisions (P1.3). Assessments are tenant-owned
/// and append-only; opportunities stay global public data.
/// </summary>
public sealed class AssessmentService(IAppDbContext db, ITenantContext tenant, IAuditLog audit, IUserDirectory users, IOptions<AssessmentOptions> options, TimeProvider clock)
{
    private static readonly JsonSerializerOptions CriteriaJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async Task<CompanyProfileDto> GetProfileAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        var profile = await db.CompanyProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.TenantId == tenantId, cancellationToken);
        if (profile is null)
        {
            return new CompanyProfileDto(new CompanyFitProfile(), null, null, true);
        }

        var names = await users.GetAsync([profile.UpdatedByUserId], cancellationToken);
        return new CompanyProfileDto(profile.Fit, profile.UpdatedAt, names.NameOf(profile.UpdatedByUserId), profile.Fit.IsEmpty);
    }

    public async Task<CompanyProfileDto> UpdateProfileAsync(CompanyFitProfile fit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fit);
        tenant.RequireRole(options.Value.MinimumRoleForProfile);
        var tenantId = tenant.TenantId;
        var userId = tenant.RequireUser();
        var now = clock.GetUtcNow();
        var profile = await db.CompanyProfiles.SingleOrDefaultAsync(p => p.TenantId == tenantId, cancellationToken);
        try
        {
            if (profile is null)
            {
                profile = new CompanyProfile(Guid.NewGuid(), tenantId, fit, userId, now);
                db.CompanyProfiles.Add(profile);
            }
            else
            {
                profile.Update(fit, userId, now);
            }
        }
        catch (DomainException ex)
        {
            throw new RequestValidationException("company_profile.invalid", ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.CompanyProfileUpdated, "company_profile", tenantId.ToString(), AuditOutcome.Success, "ok", cancellationToken);
        return await GetProfileAsync(cancellationToken);
    }

    /// <summary>Calculates the current fit without saving anything.</summary>
    public async Task<FitResult> PreviewAsync(Guid opportunityId, CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        var (_, result) = await ScoreAsync(tenantId, opportunityId, cancellationToken);
        return result;
    }

    public async Task<AssessmentDto> RecordAsync(Guid opportunityId, RecordAssessmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!Enum.IsDefined(command.Decision))
        {
            throw new RequestValidationException("assessment.invalid", "Unknown decision.");
        }

        var isDecision = command.Decision is GoNoGoDecision.Go or GoNoGoDecision.NoGo;
        var required = isDecision ? options.Value.MinimumRoleForDecision : CompanyRole.ProposalWriter;
        if (!tenant.HasTenant || tenant.Role is null || tenant.Role < required)
        {
            await audit.RecordAsync(AuditActions.AssessmentRecorded, "opportunity", opportunityId.ToString(), AuditOutcome.Denied, "role.insufficient", cancellationToken);
            tenant.RequireRole(required);
        }

        var tenantId = tenant.TenantId;
        var userId = tenant.RequireUser();
        var (opportunity, fit) = await ScoreAsync(tenantId, opportunityId, cancellationToken);
        var sequence = 1 + await db.OpportunityAssessments.CountAsync(a => a.TenantId == tenantId && a.OpportunityId == opportunityId, cancellationToken);

        OpportunityAssessment assessment;
        try
        {
            assessment = new OpportunityAssessment(
                Guid.NewGuid(), tenantId, opportunityId, sequence, opportunity.RevisionNumber, fit.Score, fit.Band, fit.RuleVersion,
                JsonSerializer.Serialize(fit.Criteria, CriteriaJson), command.Decision, command.Rationale, userId, clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            throw new RequestValidationException("assessment.invalid", ex.Message);
        }

        db.OpportunityAssessments.Add(assessment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two people recorded at the same moment: the unique (tenant, opportunity, sequence) index rejected one.
            throw new ConflictException("Another assessment was recorded at the same time. Reload and try again.");
        }

        await audit.RecordAsync(AuditActions.AssessmentRecorded, "opportunity", opportunityId.ToString(), AuditOutcome.Success, $"decision.{command.Decision}", cancellationToken);
        var names = await users.GetAsync([userId], cancellationToken);
        return ToDto(assessment, names);
    }

    /// <summary>The company's full assessment history for one opportunity, newest first.</summary>
    public async Task<IReadOnlyList<AssessmentDto>> HistoryAsync(Guid opportunityId, CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        var rows = await db.OpportunityAssessments.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.OpportunityId == opportunityId)
            .OrderByDescending(a => a.Sequence)
            .Take(200)
            .ToListAsync(cancellationToken);
        var names = await users.GetAsync(rows.Select(r => r.CreatedByUserId).Distinct().ToList(), cancellationToken);
        return rows.Select(r => ToDto(r, names)).ToList();
    }

    /// <summary>The latest assessment per opportunity for the company (the pursuit pipeline), optionally by decision.</summary>
    public async Task<IReadOnlyList<AssessmentSummaryDto>> ListLatestAsync(GoNoGoDecision? decision, CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenant();
        var latest = db.OpportunityAssessments.AsNoTracking()
            .Where(a => a.TenantId == tenantId && !db.OpportunityAssessments.Any(b => b.TenantId == tenantId && b.OpportunityId == a.OpportunityId && b.Sequence > a.Sequence));
        if (decision is { } wanted)
        {
            latest = latest.Where(a => a.Decision == wanted);
        }

        return await (from a in latest
                      join o in db.Opportunities.AsNoTracking() on a.OpportunityId equals o.Id
                      orderby a.CreatedAt descending
                      select new AssessmentSummaryDto(o.Id, o.Title, o.Deadline.DueAt, a.Score, a.Band, a.Decision, a.CreatedAt, o.RevisionNumber > a.OpportunityRevisionNumber))
            .Take(500)
            .ToListAsync(cancellationToken);
    }

    private static AssessmentDto ToDto(OpportunityAssessment a, IReadOnlyDictionary<string, DirectoryUser> names) =>
        new(a.Id, a.OpportunityId, a.Sequence, a.OpportunityRevisionNumber, a.Score, a.Band, a.RuleVersion,
            JsonSerializer.Deserialize<List<FitCriterionResult>>(a.CriteriaJson, CriteriaJson) ?? [], a.Decision, a.Rationale, names.NameOf(a.CreatedByUserId), a.CreatedAt);

    private async Task<(Opportunity Opportunity, FitResult Fit)> ScoreAsync(Guid tenantId, Guid opportunityId, CancellationToken cancellationToken)
    {
        var opportunity = await db.Opportunities.AsNoTracking().SingleOrDefaultAsync(o => o.Id == opportunityId, cancellationToken)
            ?? throw new ResourceNotFoundException();
        var profile = await db.CompanyProfiles.AsNoTracking().Where(p => p.TenantId == tenantId).Select(p => p.Fit).SingleOrDefaultAsync(cancellationToken)
            ?? new CompanyFitProfile();
        var facts = new OpportunityFacts(opportunity.Title, opportunity.Description, opportunity.Agency, opportunity.NaicsCode, opportunity.SetAside, opportunity.PlaceOfPerformanceState, opportunity.NoticeType, opportunity.Deadline.DueAt);
        return (opportunity, FitScorer.Score(profile, facts, clock.GetUtcNow()));
    }
}
