using GovBidPro.Application.SavedSearches;
using GovBidPro.Web.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovBidPro.Web.Controllers;

[ApiController]
[Route("api/v1/saved-searches")]
[Authorize(Policy = Policies.TenantMember)]
public sealed class SavedSearchesController(SavedSearchService searches) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<SavedSearchDto>> List(CancellationToken cancellationToken) => searches.ListAsync(cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<SavedSearchDto> Get(Guid id, CancellationToken cancellationToken) => searches.GetAsync(id, cancellationToken);

    [HttpPost]
    public async Task<ActionResult<SavedSearchDto>> Create(CreateSavedSearchCommand command, CancellationToken cancellationToken)
    {
        var created = await searches.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await searches.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
