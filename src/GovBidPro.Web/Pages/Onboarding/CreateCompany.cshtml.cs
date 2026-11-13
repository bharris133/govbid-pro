using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using GovBidPro.Application.Common;
using GovBidPro.Application.Companies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GovBidPro.Web.Pages.Onboarding;

public sealed class CreateCompanyModel(CompanyService companies) : PageModel
{
    [BindProperty]
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string CompanyName { get; set; } = string.Empty;

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        try
        {
            await companies.CreateCompanyAsync(userId, CompanyName, cancellationToken);
        }
        catch (ConflictException)
        {
            return Redirect("/Opportunities");
        }
        catch (RequestValidationException ex)
        {
            ModelState.AddModelError(nameof(CompanyName), ex.Message);
            return Page();
        }

        return Redirect("/Opportunities");
    }
}
