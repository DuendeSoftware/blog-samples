# WhatsApp OTP with Duende IdentityServer + User Management

> Companion sample for the blog post
> [WhatsApp One-Time Password (OTP) Login with Duende IdentityServer and User Management](https://duendesoftware.com/blog/)
> on the Duende blog. Sample originally built by
> [Matthijs Hoekstra](https://github.com/mahoekst), Product Manager at Duende.

A Duende IdentityServer app that signs users in with a **one-time password (OTP)
delivered over WhatsApp**. It uses [Duende User Management](https://docs.duendesoftware.com/identityserver/usermanagement/)
for the OTP flow and a **custom `IOtpDispatcher`** that sends the code through the
[Meta WhatsApp Business Cloud API](https://developers.facebook.com/docs/whatsapp/cloud-api)
using an approved **authentication** message template.

## What's in the project

| File                                                                                               | Purpose                                                                                              |
|----------------------------------------------------------------------------------------------------|------------------------------------------------------------------------------------------------------|
| [Program.cs](Program.cs)                                                                           | Wires up IdentityServer, User Management (SQLite store), and the WhatsApp dispatcher.                |
| [Config.cs](Config.cs)                                                                             | In-memory IdentityServer clients, resources, and scopes.                                             |
| [WhatsApp/WhatsAppOtpDispatcher.cs](WhatsApp/WhatsAppOtpDispatcher.cs)                             | The custom `IOtpDispatcher` that posts an authentication template message to the WhatsApp Cloud API. |
| [WhatsApp/WhatsAppOptions.cs](WhatsApp/WhatsAppOptions.cs)                                         | Strongly-typed configuration (phone number ID, token, template name/language).                       |
| [WhatsApp/WhatsAppServiceCollectionExtensions.cs](WhatsApp/WhatsAppServiceCollectionExtensions.cs) | Registers the typed `HttpClient` and the dispatcher.                                                 |
| [Pages/Account/Login.cshtml](Pages/Account/Login.cshtml)                                           | Enter a phone number, send the OTP.                                                                  |
| [Pages/Account/EnterOtp.cshtml](Pages/Account/EnterOtp.cshtml)                                     | Enter the code, verify, and sign in.                                                                 |
| [Pages/Account/Logout.cshtml](Pages/Account/Logout.cshtml)                                         | Sign out.                                                                                            |

### How the custom dispatcher hooks in

Duende User Management calls `IOtpSender.TrySendOtpAsync(...)`, which generates a
code and hands it to the registered `IOtpDispatcher`. Our
[`WhatsAppOtpDispatcher`](WhatsApp/WhatsAppOtpDispatcher.cs):

- **`CanDispatch`** returns `true` only for `OtpChannel.Sms` addresses (we treat
  those as phone numbers deliverable over WhatsApp). Email addresses fall through
  to any other dispatcher you register.
- **`DispatchAsync`** normalizes the phone number to digits and `POST`s an approved
  **authentication template** message to `/{PhoneNumberId}/messages`, injecting the
  code into the template body and the OTP button.

It is registered in `Program.cs` via `builder.Services.AddWhatsAppOtpDispatcher(builder.Configuration)`.

---

## Part 1 — WhatsApp / Meta setup

You need a Meta developer account, a **verified** Meta business, and a WhatsApp
Business Account (WABA) with a phone number.

### 1. Create a Meta app

1. Go to <https://developers.facebook.com/apps> → **Create app**.
2. Choose the **Business** app type.
3. On the app dashboard, find **WhatsApp** and click **Set up**. This creates a
   WhatsApp Business Account and a sender phone number.

### 2. Note your IDs

Open **WhatsApp → API Setup** in the dashboard. Note:

- **Phone number ID** — the numeric ID of the sender (this is what goes in
  `WhatsApp:PhoneNumberId`, *not* the phone number itself).
- **WhatsApp Business Account ID (WABA ID)** — needed to manage templates.

### 3. Create a permanent access token

Create a **System User** token (these do not expire):

1. **Business Settings → Users → System users → Add** (create an Admin system user).
2. **Add assets** → assign **both** your app *and* the specific **WhatsApp account
   (WABA)** that holds your sender number to the system user, each with **Full
   control**. This asset assignment is essential: without it the token gets
   `(#100) ... does not exist, cannot be loaded due to missing permissions`
   (error subcode 33) for that number/WABA even though the scopes look correct.
3. **Generate new token** → select your app → grant
   **`whatsapp_business_messaging`**, **`whatsapp_business_management`**, and
   **`business_management`** permissions.
4. Copy the token and store it as a secret (see Part 2).

> **A Phone Number ID is not enough on its own** — the token must be assigned to
> the WABA that owns it. To confirm a token can see a number:
> `curl -s "https://graph.facebook.com/v21.0/<PHONE_NUMBER_ID>?fields=id,display_phone_number,code_verification_status,account_mode" -H "Authorization: Bearer <TOKEN>"`.
> A real JSON object (not an error) means access is correctly assigned.

### 4. Create the authentication template

OTP messages must use a pre-approved **authentication** category template.
Templates are **per-WABA**, so create it on the WABA that owns your sender number.

**Option A — Graph API (one call, typically auto-approved instantly):**

```bash
curl -X POST "https://graph.facebook.com/v21.0/<WABA_ID>/message_templates" \
  -H "Authorization: Bearer <ACCESS_TOKEN>" -H "Content-Type: application/json" \
  -d '{
    "name":"otp_code","language":"en_US","category":"AUTHENTICATION",
    "message_send_ttl_seconds":300,
    "components":[
      {"type":"BODY","add_security_recommendation":true},
      {"type":"FOOTER","code_expiration_minutes":5},
      {"type":"BUTTONS","buttons":[{"type":"OTP","otp_type":"COPY_CODE"}]}
    ]}'
```

**Option B — WhatsApp Manager (UI):**

1. Go to **WhatsApp Manager → Account tools → Message templates → Create template**
   (<https://business.facebook.com/wa/manage/message-templates/>).
2. **Category:** `Authentication`.
3. **Name:** `otp_code` (must match `WhatsApp:TemplateName`; lowercase + underscores).
4. **Language:** e.g. English (US) → code `en_US` (must match `WhatsApp:TemplateLanguage`).
5. **Button:** add a **"Copy code"** button.
6. Submit. Authentication templates are usually approved within minutes.

> **`TemplateName` and `TemplateLanguage` must match the approved template
> exactly.** The language is the full locale code (e.g. `en_US` or `en`) — a
> mismatch fails with `(#132001) Template name does not exist in <language>`.

---

## Part 2 — Configure the application

The WhatsApp settings live under the `WhatsApp` section. Non-secret defaults are in
[appsettings.json](appsettings.json):

```json
{
  "WhatsApp": {
    "GraphApiBaseUrl": "https://graph.facebook.com/v21.0/",
    "PhoneNumberId": "",
    "AccessToken": "",
    "TemplateName": "otp_code",
    "TemplateLanguage": "en_US",
    "ButtonSubType": "url"
  }
}
```

**Do not commit the access token.** Store secrets with the .NET user-secrets tool:

```bash
cd WhatsAppOtpUserManagement
dotnet user-secrets init
dotnet user-secrets set "WhatsApp:PhoneNumberId" "<YOUR_PHONE_NUMBER_ID>"
dotnet user-secrets set "WhatsApp:AccessToken"   "<YOUR_ACCESS_TOKEN>"
```

| Setting | Description |
| ------- | ----------- |
| `GraphApiBaseUrl` | Graph API base URL, including version. Update the version as Meta releases new ones. |
| `PhoneNumberId` | The sender's Phone Number ID from **WhatsApp → API Setup**. |
| `AccessToken` | System user (permanent) access token. |
| `TemplateName` | Must exactly match the approved template name. |
| `TemplateLanguage` | Must match the template's language code (e.g. `en_US`). |
| `ButtonSubType` | Must be `url` on the send request — the Cloud API rejects `copy_code` even for Copy-code buttons. See [Meta: Copy code authentication templates](https://developers.facebook.com/documentation/business-messaging/whatsapp/templates/authentication-templates/copy-code-button-authentication-templates). |

---

## Part 3 — Run

```bash
cd WhatsAppOtpUserManagement
dotnet run                      # uses the "https" launch profile by default
```

> **Run over HTTPS.** IdentityServer issues its authentication cookie with
> `Secure` + `SameSite=None`, which browsers **silently drop over plain HTTP**.
> If you run on `http://` only, entering the correct OTP appears to do nothing —
> you're redirected straight back to the login page because the sign-in cookie is
> never stored. The app is configured to listen on **`https://localhost:5001`**
> (and `http://localhost:5000`); always use the HTTPS URL. If prompted, trust the
> ASP.NET Core dev certificate once with `dotnet dev-certs https --trust`.

Then:

1. Browse to **`https://localhost:5001`**. You are redirected to `/Account/Login`.
2. Enter a phone number **in E.164 format** (e.g. `+15551234567`).
3. Click **Send one-time password via WhatsApp**. Check WhatsApp on that device for
   the code.
4. Enter the code on `/Account/EnterOtp` and sign in. New numbers are
   auto-registered on first successful login.

---

## The API request the dispatcher sends

For reference, `DispatchAsync` sends this to `POST {GraphApiBaseUrl}{PhoneNumberId}/messages`
with an `Authorization: Bearer <token>` header:

```json
{
  "messaging_product": "whatsapp",
  "recipient_type": "individual",
  "to": "15551234567",
  "type": "template",
  "template": {
    "name": "otp_code",
    "language": { "code": "en_US" },
    "components": [
      { "type": "body", "parameters": [ { "type": "text", "text": "123456" } ] },
      {
        "type": "button",
        "sub_type": "url",
        "index": "0",
        "parameters": [ { "type": "text", "text": "123456" } ]
      }
    ]
  }
}
```

The OTP code appears **twice** (body + button) — this is required by Meta's
authentication template format. If the API returns a non-success status, the
dispatcher logs the response body and throws, which surfaces as a failed send in
the login flow.

---

## Troubleshooting

- **`(#132001) Template name does not exist in <language>`** — `TemplateName` or
  `TemplateLanguage` doesn't match the approved template exactly, or the template
  lives on a different WABA than the sender number.
- **`(#132018) Button at index 0 must be of type Url`** — set
  `WhatsApp:ButtonSubType` to `url`. The send request always uses `url`, even when
  the template button is a Copy-code button.
- **`(#100) ... does not exist, cannot be loaded due to missing permissions`
  (subcode 33)** — the token isn't assigned to the WABA/number. Assign the WhatsApp
  account to the system user (Part 1, step 3).
- **`401 Unauthorized`** — expired or invalid token, or missing
  `whatsapp_business_messaging` permission; use a permanent system-user token.

## Production notes

- Switch the SQLite store to PostgreSQL or SQL Server for production
  (`Duende.Storage.Postgresql` / `Duende.Storage.Mssql`).
- Persist Data Protection keys to a shared, durable location for multi-instance
  deployments.
- Add IdentityServer license configuration for production use.
- Mind WhatsApp's per-number messaging tier limits as volume grows.
