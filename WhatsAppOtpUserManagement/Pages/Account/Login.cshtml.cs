using System.ComponentModel.DataAnnotations;
using Duende.UserManagement;
using Duende.UserManagement.Authentication.Otp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WhatsAppOtpIdentityServer.Pages.Account;

public class LoginModel(IOtpSender otpSender, ILogger<LoginModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required]
        [Phone]
        [Display(Name = "Phone number")]
        public string PhoneNumber { get; set; } = string.Empty;
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!PhoneNumber.TryCreate(Input.PhoneNumber, out var phoneNumber))
        {
            ModelState.AddModelError(nameof(Input.PhoneNumber), "Invalid phone number format.");
            return Page();
        }

        SendOtpResult result;
        try
        {
            result = await otpSender.TrySendOtpAsync(
                new OtpAddress(OtpChannel.Sms, phoneNumber),
                HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            // A misconfigured or unapproved WhatsApp template, an invalid token, or
            // a network failure surfaces here. Log the detail and show a friendly
            // message instead of a 500.
            logger.LogError(ex, "Failed to send WhatsApp OTP.");
            ModelState.AddModelError(string.Empty,
                "We couldn't send the code right now. Please try again later.");
            return Page();
        }

        if (result is SendOtpResult.Sent sentResult)
        {
            TempData["OtpToken"] = sentResult.Token.Value.ToString();
            TempData["ReturnUrl"] = returnUrl;
            return RedirectToPage("/Account/EnterOtp");
        }

        if (result is SendOtpResult.Blocked blocked)
        {
            var blockedFor = blocked.SendingBlockedUntilUtc - DateTimeOffset.UtcNow;
            var blockedMessage = $"Too many attempts. Try again in {Math.Ceiling(blockedFor.TotalSeconds)} second(s).";
            ModelState.AddModelError(string.Empty, blockedMessage);
            return Page();
        }

        ModelState.AddModelError(string.Empty, "Failed to send one-time password.");
        return Page();
    }
}
