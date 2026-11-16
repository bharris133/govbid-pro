using GovBidPro.Application.Common;
using GovBidPro.Application.Documents;
using GovBidPro.Domain.Enums;
using GovBidPro.Web.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovBidPro.Web.Controllers;

public sealed class UploadDocumentForm
{
    public IFormFile? File { get; set; }

    public DataClassification Classification { get; set; }

    public bool AffirmPermittedToUpload { get; set; }

    public bool AffirmNoProhibitedContent { get; set; }

    /// <summary>Optional: link the document to one of the company's proposals.</summary>
    public Guid? ProposalId { get; set; }

    /// <summary>Optional: mark as a reusable company template.</summary>
    public bool IsTemplate { get; set; }
}

public sealed class UploadVersionForm
{
    public IFormFile? File { get; set; }

    public bool AffirmPermittedToUpload { get; set; }

    public bool AffirmNoProhibitedContent { get; set; }

    public string? Note { get; set; }
}

[ApiController]
[Route("api/v1/documents")]
[Authorize(Policy = Policies.TenantMember)]
public sealed class DocumentsController(DocumentService documents) : ControllerBase
{
    /// <summary>Hard transport cap; the configured Documents:MaxUploadBytes is enforced by the service.</summary>
    private const long TransportLimitBytes = 30 * 1024 * 1024;

    [HttpGet]
    public Task<IReadOnlyList<DocumentDto>> List([FromQuery] Guid? proposalId, [FromQuery] bool templates, CancellationToken cancellationToken) =>
        documents.ListAsync(new DocumentFilter(proposalId, templates), cancellationToken);

    [HttpPost]
    [RequestSizeLimit(TransportLimitBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = TransportLimitBytes)]
    public async Task<ActionResult<DocumentDto>> Upload([FromForm] UploadDocumentForm form, CancellationToken cancellationToken)
    {
        if (form.File is null)
        {
            throw new RequestValidationException("upload.file_missing", "A file is required.");
        }

        await using var stream = form.File.OpenReadStream();
        var declaration = new UploadDeclaration(form.Classification, form.AffirmPermittedToUpload, form.AffirmNoProhibitedContent);
        var result = await documents.UploadAsync(new UploadDocumentCommand(form.File.FileName, form.File.ContentType, form.File.Length, stream, declaration, form.ProposalId, form.IsTemplate), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Authorized streaming download. No public or long-lived URLs are issued.</summary>
    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var download = await documents.OpenDownloadAsync(id, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return File(download.Content, download.ContentType, download.FileName);
    }

    [HttpGet("{id:guid}/versions")]
    public Task<IReadOnlyList<DocumentVersionDto>> Versions(Guid id, CancellationToken cancellationToken) => documents.ListVersionsAsync(id, cancellationToken);

    /// <summary>Uploads a new version. It passes the same screening as a first upload and becomes current.</summary>
    [HttpPost("{id:guid}/versions")]
    [RequestSizeLimit(TransportLimitBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = TransportLimitBytes)]
    public async Task<ActionResult<DocumentDto>> UploadVersion(Guid id, [FromForm] UploadVersionForm form, CancellationToken cancellationToken)
    {
        if (form.File is null)
        {
            throw new RequestValidationException("upload.file_missing", "A file is required.");
        }

        await using var stream = form.File.OpenReadStream();
        var result = await documents.UploadVersionAsync(id, new UploadVersionCommand(form.File.FileName, form.File.ContentType, form.File.Length, stream, form.AffirmPermittedToUpload, form.AffirmNoProhibitedContent, form.Note), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("{id:guid}/versions/{number:int}/content")]
    public async Task<IActionResult> DownloadVersion(Guid id, int number, CancellationToken cancellationToken)
    {
        var download = await documents.OpenVersionDownloadAsync(id, number, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return File(download.Content, download.ContentType, download.FileName);
    }

    /// <summary>Links the document to a proposal and/or marks it as a template.</summary>
    [HttpPut("{id:guid}/placement")]
    public Task<DocumentDto> Place(Guid id, PlaceDocumentCommand command, CancellationToken cancellationToken) => documents.PlaceAsync(id, command, cancellationToken);

    /// <summary>Copies the current version (usually of a template) into a new document.</summary>
    [HttpPost("{id:guid}/copy")]
    public async Task<ActionResult<DocumentDto>> Copy(Guid id, CopyDocumentCommand command, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await documents.CopyAsync(id, command, cancellationToken));

    [HttpDelete("{id:guid}")]
    public Task<DocumentDto> Delete(Guid id, CancellationToken cancellationToken) => documents.DeleteAsync(id, cancellationToken);

    [HttpPost("{id:guid}/restore")]
    public Task<DocumentDto> Restore(Guid id, CancellationToken cancellationToken) => documents.RestoreAsync(id, cancellationToken);

    [HttpPost("{id:guid}/quarantine")]
    public Task<DocumentDto> Quarantine(Guid id, CancellationToken cancellationToken) => documents.QuarantineAsync(id, cancellationToken);
}
