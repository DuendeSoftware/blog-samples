using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Duende.IdentityServer.Services;
using Duende.UserManagement.Authentication.Otp;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WhatsAppOtpIdentityServer.Pages.Account;

public class EnterOtpModel(
    IOtpAuthenticator otpAuthenticator,
    IIdentityServerInteractionService interaction) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required]
        public string Token { get; set; } = string.Empty;

        [Required]
        public string Code { get; set; } = string.Empty;
    }

    public IActionResult OnGet()
    {
        var token = TempData["OtpToken"]?.ToString();
        if (token is null)
        {
            return RedirectToPage("/Account/Login");
        }

        Input.Token = token;
        // Preserve values across the POST round-trip.
        TempData.Keep("OtpToken");
        TempData.Keep("ReturnUrl");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Input.Code) || string.IsNullOrWhiteSpace(Input.Token))
        {
            ModelState.AddModelError(string.Empty, "Invalid input.");
            return Page();
        }

        var otp = PlainTextOtp.Create(Input.Code);
        var token = OtpToken.Create(Input.Token);

        var authResult = await otpAuthenticator.TryAuthenticateAsync(
            otp, token, HttpContext.RequestAborted);

        if (authResult is not OtpAuthenticationResult.Success otpSuccess)
        {
            ModelState.AddModelError(string.Empty, "Invalid or expired code. Please try again.");
            TempData.Keep("ReturnUrl");
            return Page();
        }

        var claims = new List<Claim>
        {
            new("sub", otpSuccess.UserSubjectId.ToString()!),
            new(ClaimTypes.Name, otpSuccess.Address.SubjectId.ToString()!),
        };

        var identity = new ClaimsIdentity(claims, "otp");
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            Duende.IdentityServer.IdentityServerConstants.DefaultCookieAuthenticationScheme,
            principal,
            new AuthenticationProperties());

        var returnUrl = TempData["ReturnUrl"]?.ToString();
        returnUrl = interaction.IsValidReturnUrl(returnUrl) ? returnUrl : "~/";
        return LocalRedirect(returnUrl!);
    }
}
