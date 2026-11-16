using GovBidPro.Application.Assessments;
using GovBidPro.Domain.Entities;
using GovBidPro.Web.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovBidPro.Web.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize(Policy = Policies.TenantMember)]
public sealed class AssessmentsController(AssessmentService assessments) : ControllerBase
{
    [HttpGet("company/profile")]
    public Task<CompanyProfileDto> GetProfile(CancellationToken cancellationToken) => assessments.GetProfileAsync(cancellationToken);

    [HttpPut("company/profile")]
    public Task<CompanyProfileDto> UpdateProfile(CompanyFitProfile fit, CancellationToken cancellationToken) => assessments.UpdateProfileAsync(fit, cancellationToken);

    /// <summary>Current explainable fit score for the caller's company. Nothing is saved.</summary>
    [HttpGet("opportunities/{id:guid}/fit")]
    public Task<FitResult> Fit(Guid id, CancellationToken cancellationToken) => assessments.PreviewAsync(id, cancellationToken);

    [HttpGet("opportunities/{id:guid}/assessments")]
    public Task<IReadOnlyList<AssessmentDto>> History(Guid id, CancellationToken cancellationToken) => assessments.HistoryAsync(id, cancellationToken);

    /// <summary>Scores the opportunity now and appends an immutable assessment with the decision.</summary>
    [HttpPost("opportunities/{id:guid}/assessments")]
    public async Task<ActionResult<AssessmentDto>> Record(Guid id, RecordAssessmentCommand command, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await assessments.RecordAsync(id, command, cancellationToken));

    /// <summary>Latest assessment per opportunity for the company.</summary>
    [HttpGet("assessments")]
    public Task<IReadOnlyList<AssessmentSummaryDto>> Latest([FromQuery] GoNoGoDecision? decision, CancellationToken cancellationToken) => assessments.ListLatestAsync(decision, cancellationToken);
}
