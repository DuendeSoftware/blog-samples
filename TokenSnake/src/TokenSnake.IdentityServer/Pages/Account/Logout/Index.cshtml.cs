using Microsoft.AspNetCore.Authentication;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TokenSnake.IdentityServer.Pages.Account.Logout;

/// <summary>
/// The logout page IdentityServer redirects to from the end-session endpoint
/// (https://docs.duendesoftware.com/identityserver/ui/logout/).
/// </summary>
public class IndexModel(IIdentityServerInteractionService interaction) : PageModel
{
    public async Task<IActionResult> OnGet(string? logoutId)
    {
        // Read the context first: it holds the post_logout_redirect_uri that IdentityServer already validated
        // against the client's PostLogoutRedirectUris, so we never redirect to a URL we made up.
        var context = await interaction.GetLogoutContextAsync(logoutId, HttpContext.RequestAborted);

        // Normally a logout page asks "are you sure?" first (ShowSignoutPrompt), because a GET that signs you
        // out can be triggered by any other site (logout CSRF). Here the worst case is an annoyed player who
        // has to type a nickname again: there is no account, no data, nothing to lose. So this demo signs out
        // without a prompt. A real application should keep the prompt, or at least only do this for
        // requests that carry a valid id_token_hint.
        await HttpContext.SignOutAsync();

        return context?.PostLogoutRedirectUri is { Length: > 0 } uri ? Redirect(uri) : Redirect("~/");
    }
}
