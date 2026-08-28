namespace WhatsAppOtpIdentityServer.WhatsApp;

/// <summary>
/// Configuration for delivering OTP codes through the Meta WhatsApp Business
/// Cloud API. Bind this from the "WhatsApp" section of appsettings.json.
/// </summary>
public class WhatsAppOptions
{
    /// <summary>
    /// Graph API base address. Defaults to the current stable Graph API version.
    /// </summary>
    public string GraphApiBaseUrl { get; set; } = "https://graph.facebook.com/v21.0/";

    /// <summary>
    /// The Phone Number ID of the WhatsApp sender (from the Meta app dashboard,
    /// WhatsApp &gt; API Setup). This is NOT the phone number itself.
    /// </summary>
    public string PhoneNumberId { get; set; } = string.Empty;

    /// <summary>
    /// A permanent (System User) access token with the whatsapp_business_messaging
    /// permission. Store this as a secret, not in source control.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// The name of the approved WhatsApp "authentication" message template that
    /// contains a one-time-password copy-code button.
    /// </summary>
    public string TemplateName { get; set; } = "otp_code";

    /// <summary>
    /// The BCP-47 language/locale code the template was approved in (e.g. "en_US").
    /// </summary>
    public string TemplateLanguage { get; set; } = "en_US";

    /// <summary>
    /// The OTP button sub_type used when sending an authentication template.
    /// The WhatsApp Cloud API requires "url" for both "Copy code" and
    /// "One-tap autofill" OTP buttons on the message-send request (the button's
    /// visible behavior is fixed by how the template was created in the Meta
    /// dashboard, not by this value).
    /// <para>
    /// The template is created with button type "otp"/"copy_code", but Meta
    /// stores it as a URL button, so the send request must use "url". Sending
    /// "copy_code" fails with "(#132018) Button at index 0 must be of type Url".
    /// See Meta's Copy code authentication templates docs:
    /// https://developers.facebook.com/documentation/business-messaging/whatsapp/templates/authentication-templates/copy-code-button-authentication-templates
    /// </para>
    /// </summary>
    public string ButtonSubType { get; set; } = "url";
}
