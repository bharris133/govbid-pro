using GovBidPro.Application.Companies;
using GovBidPro.Application.Proposals;
using GovBidPro.Web.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovBidPro.Web.Controllers;

[ApiController]
[Route("api/v1/proposals")]
[Authorize(Policy = Policies.TenantMember)]
public sealed class ProposalsController(ProposalService proposals) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ProposalSummaryDto>> List([FromQuery] bool includeClosed, CancellationToken cancellationToken) => proposals.ListAsync(includeClosed, cancellationToken);

    [HttpPost]
    public async Task<ActionResult<ProposalDetailDto>> Create(CreateProposalCommand command, CancellationToken cancellationToken)
    {
        var created = await proposals.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet("{id:guid}")]
    public Task<ProposalDetailDto> Get(Guid id, CancellationToken cancellationToken) => proposals.GetAsync(id, cancellationToken);

    [HttpPut("{id:guid}")]
    public Task<ProposalDetailDto> Update(Guid id, UpdateProposalCommand command, CancellationToken cancellationToken) => proposals.UpdateAsync(id, command, cancellationToken);

    [HttpPost("{id:guid}/status")]
    public Task<ProposalDetailDto> ChangeStatus(Guid id, ChangeStatusCommand command, CancellationToken cancellationToken) => proposals.ChangeStatusAsync(id, command, cancellationToken);

    [HttpPost("{id:guid}/sections")]
    public Task<ProposalDetailDto> AddSection(Guid id, SectionCommand command, CancellationToken cancellationToken) => proposals.AddSectionAsync(id, command, cancellationToken);

    [HttpPut("{id:guid}/sections/{sectionId:guid}")]
    public Task<ProposalDetailDto> UpdateSection(Guid id, Guid sectionId, SectionCommand command, CancellationToken cancellationToken) => proposals.UpdateSectionAsync(id, sectionId, command, cancellationToken);

    /// <summary>The compliance matrix.</summary>
    [HttpGet("{id:guid}/requirements")]
    public async Task<IReadOnlyList<RequirementDto>> Requirements(Guid id, CancellationToken cancellationToken) => (await proposals.GetAsync(id, cancellationToken)).Requirements;

    [HttpPost("{id:guid}/requirements")]
    public async Task<ActionResult<RequirementDto>> AddRequirement(Guid id, RequirementCommand command, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await proposals.AddRequirementAsync(id, command, cancellationToken));

    [HttpPut("{id:guid}/requirements/{requirementId:guid}")]
    public Task<RequirementDto> UpdateRequirement(Guid id, Guid requirementId, RequirementCommand command, CancellationToken cancellationToken) =>
        proposals.UpdateRequirementAsync(id, requirementId, command, cancellationToken);

    [HttpDelete("{id:guid}/requirements/{requirementId:guid}")]
    public async Task<IActionResult> DeleteRequirement(Guid id, Guid requirementId, CancellationToken cancellationToken)
    {
        await proposals.DeleteRequirementAsync(id, requirementId, cancellationToken);
        return NoContent();
    }

    /// <summary>Requests a review from a named member. The proposal must be in review.</summary>
    [HttpPost("{id:guid}/reviews")]
    public Task<ProposalDetailDto> RequestReview(Guid id, RequestReviewCommand command, CancellationToken cancellationToken) => proposals.RequestReviewAsync(id, command, cancellationToken);

    /// <summary>The assigned reviewer approves or requests changes.</summary>
    [HttpPost("{id:guid}/reviews/{reviewId:guid}/decision")]
    public Task<ProposalDetailDto> DecideReview(Guid id, Guid reviewId, DecideReviewCommand command, CancellationToken cancellationToken) => proposals.DecideReviewAsync(id, reviewId, command, cancellationToken);

    /// <summary>Compliance matrix as CSV (formula-neutralized). Generated on request, never cached.</summary>
    [HttpGet("{id:guid}/exports/compliance-matrix.csv")]
    public async Task<IActionResult> ExportMatrix(Guid id, [FromServices] ProposalExportService exports, CancellationToken cancellationToken) =>
        Download(await exports.ComplianceMatrixCsvAsync(id, cancellationToken));

    /// <summary>Working outline (sections, owners, requirements) as Markdown.</summary>
    [HttpGet("{id:guid}/exports/outline.md")]
    public async Task<IActionResult> ExportOutline(Guid id, [FromServices] ProposalExportService exports, CancellationToken cancellationToken) =>
        Download(await exports.OutlineMarkdownAsync(id, cancellationToken));

    private FileContentResult Download(ExportFile file)
    {
        Response.Headers.CacheControl = "no-store";
        return File(file.Content, file.ContentType, file.FileName);
    }
}

[ApiController]
[Route("api/v1/company/members")]
[Authorize(Policy = Policies.TenantMember)]
public sealed class MembersController(MemberService members) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<MemberDto>> List(CancellationToken cancellationToken) => members.ListActiveAsync(cancellationToken);
}
