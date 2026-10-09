using System.Security.Claims;
using Duende.IdentityServer;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TokenSnake.IdentityServer.Pages.Account.Login;

public class IndexModel(IIdentityServerInteractionService interaction, NicknameBlocklist blocklist) : PageModel
{
    [BindProperty] public string? Nickname { get; set; }
    [BindProperty] public string? ReturnUrl { get; set; }

    /// <summary>Shown for problems that don't belong to the nickname field.</summary>
    public string? Message { get; private set; }

    public void OnGet(string? returnUrl) => ReturnUrl = returnUrl;

    public async Task<IActionResult> OnPostAsync()
    {
        // Never redirect anywhere IdentityServer didn't hand us (open-redirect protection).
        if (!interaction.IsValidReturnUrl(ReturnUrl))
        {
            Message = "This sign-in link is not valid. Start again from the game.";
            return Page();
        }

        if (!TokenSnake.IdentityServer.Nickname.TryCreate(Nickname, out var nick, out var error, blocklist.Words))
        {
            ModelState.AddModelError(nameof(Nickname), error!);
            return Page();
        }

        // Two logins with the same nickname are different players, so every sub gets a fresh GUID.
        var sub = $"{nick}:{Guid.NewGuid():N}";
        var user = new IdentityServerUser(sub)
        {
            DisplayName = nick,
            AdditionalClaims = { new Claim("name", nick), new Claim(SnakeNames.Nickname, nick) },
        };
        await HttpContext.SignInAsync(user);

        return Redirect(ReturnUrl!);
    }
}
