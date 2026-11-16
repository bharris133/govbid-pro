using System.Security.Claims;
using GovBidPro.Application.Collaboration;
using GovBidPro.Application.Common;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GovBidPro.Web.Pages.Invitations;

/// <summary>
/// Accepts a company invitation. Requires sign-in (the folder is authorized); the token arrives in the
/// query string from the emailed link and is posted back with the antiforgery token.
/// </summary>
public sealed class AcceptModel(InvitationService invitations) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    public InvitationPreviewDto? Invitation { get; private set; }

    public string? Error { get; private set; }

    public string RoleText => Invitation?.Role switch
    {
        CompanyRole.CompanyAdmin => "company admin",
        CompanyRole.TeamManager => "team manager",
        CompanyRole.ProposalWriter => "proposal writer",
        _ => "reviewer",
    };

    public string StateText => Invitation?.State switch
    {
        InvitationState.Accepted => "already used",
        InvitationState.Revoked => "withdrawn",
        _ => "expired",
    };

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        // The link carries a secret; keep it out of caches and referrers.
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Invitation = await invitations.PreviewAsync(Token, cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        try
        {
            await invitations.AcceptAsync(userId, Token, cancellationToken);
            return Redirect("/Dashboard");
        }
        catch (ResourceNotFoundException)
        {
            Invitation = null;
        }
        catch (ConflictException ex)
        {
            Error = ex.Message;
        }
        catch (PolicyDeniedException ex)
        {
            Error = ex.ReasonCode == "invitation.email_not_confirmed"
                ? "Confirm your email address first (see the confirmation email), then open the invitation link again."
                : "This invitation was sent to a different email address. Sign in with the invited address.";
        }

        Invitation ??= await invitations.PreviewAsync(Token, cancellationToken);
        return Page();
    }
}
