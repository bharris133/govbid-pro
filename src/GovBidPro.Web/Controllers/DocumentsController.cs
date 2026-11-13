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
}

[ApiController]
[Route("api/v1/documents")]
[Authorize(Policy = Policies.TenantMember)]
public sealed class DocumentsController(DocumentService documents) : ControllerBase
{
    /// <summary>Hard transport cap; the configured Documents:MaxUploadBytes is enforced by the service.</summary>
    private const long TransportLimitBytes = 30 * 1024 * 1024;

    [HttpGet]
    public Task<IReadOnlyList<DocumentDto>> List(CancellationToken cancellationToken) => documents.ListAsync(cancellationToken);

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
        var result = await documents.UploadAsync(new UploadDocumentCommand(form.File.FileName, form.File.ContentType, form.File.Length, stream, declaration), cancellationToken);
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

    [HttpDelete("{id:guid}")]
    public Task<DocumentDto> Delete(Guid id, CancellationToken cancellationToken) => documents.DeleteAsync(id, cancellationToken);

    [HttpPost("{id:guid}/restore")]
    public Task<DocumentDto> Restore(Guid id, CancellationToken cancellationToken) => documents.RestoreAsync(id, cancellationToken);

    [HttpPost("{id:guid}/quarantine")]
    public Task<DocumentDto> Quarantine(Guid id, CancellationToken cancellationToken) => documents.QuarantineAsync(id, cancellationToken);
}
