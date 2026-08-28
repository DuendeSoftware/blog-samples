using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Duende.UserManagement.Authentication.Otp;
using Microsoft.Extensions.Options;

namespace WhatsAppOtpIdentityServer.WhatsApp;

/// <summary>
/// Delivers one-time passwords over WhatsApp using the Meta WhatsApp Business
/// Cloud API. It sends an approved "authentication" template message whose body
/// and OTP button are populated with the generated code.
/// </summary>
public partial class WhatsAppOtpDispatcher(
    HttpClient httpClient,
    IOptions<WhatsAppOptions> options,
    ILogger<WhatsAppOtpDispatcher> logger) : IOtpDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WhatsAppOptions _options = options.Value;

    /// <summary>
    /// This dispatcher only handles SMS-channel addresses, which we treat as
    /// phone numbers deliverable over WhatsApp. Email addresses are ignored so a
    /// different dispatcher can handle them.
    /// </summary>
    public bool CanDispatch(OtpAddress address) => address.Channel == OtpChannel.Sms;

    public async Task DispatchAsync(
        OtpAddress address,
        PlainTextOtp otp,
        TimeSpan expiresAfter,
        CancellationToken ct)
    {
        var to = NormalizePhoneNumber(address.SubjectId.ToString());
        var code = otp.Text;

        var payload = BuildTemplatePayload(to, code);

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Relative to WhatsAppOptions.GraphApiBaseUrl (set on the typed HttpClient).
        var requestUri = $"{_options.PhoneNumberId}/messages";

        using var response = await httpClient.PostAsync(requestUri, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError(
                "WhatsApp OTP delivery to {PhoneNumberId} failed with status {StatusCode}: {Body}",
                _options.PhoneNumberId, (int)response.StatusCode, body);

            throw new InvalidOperationException(
                $"WhatsApp OTP delivery failed with status {(int)response.StatusCode}.");
        }

        logger.LogInformation("WhatsApp OTP delivered successfully to recipient.");
    }

    /// <summary>
    /// Builds an approved authentication-template message. The OTP
    /// <paramref name="code"/> must appear twice — once in the body parameter and
    /// once in the button parameter — per Meta's authentication template send
    /// format. The button sub_type is "url" (see
    /// <see cref="WhatsAppOptions.ButtonSubType"/>).
    /// https://developers.facebook.com/documentation/business-messaging/whatsapp/templates/authentication-templates/copy-code-button-authentication-templates
    /// </summary>
    private object BuildTemplatePayload(string to, string code) => new
    {
        messaging_product = "whatsapp",
        recipient_type = "individual",
        to,
        type = "template",
        template = new
        {
            name = _options.TemplateName,
            language = new { code = _options.TemplateLanguage },
            components = new object[]
            {
                new
                {
                    type = "body",
                    parameters = new[] { new { type = "text", text = code } },
                },
                new
                {
                    type = "button",
                    sub_type = _options.ButtonSubType,
                    index = "0",
                    parameters = new[] { new { type = "text", text = code } },
                },
            },
        },
    };

    /// <summary>
    /// The WhatsApp Cloud API expects the recipient's number in E.164 form using
    /// only digits (country code included, no "+", spaces, or separators).
    /// </summary>
    private static string NormalizePhoneNumber(string phoneNumber)
        => NonDigits().Replace(phoneNumber, string.Empty);

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigits();
}
