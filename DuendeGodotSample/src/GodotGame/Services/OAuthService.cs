using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Duende.IdentityModel;
using Duende.IdentityModel.Client;

namespace GodotGame.Services;

/// <summary>
/// Enum returned by the device token polling loop.
/// </summary>
public enum DevicePollResult
{
    Success,
    Pending,
    SlowDown,
    Error
}

/// <summary>
/// Shared OAuth/OIDC service. Handles discovery, PKCE, authorization code exchange,
/// and device authorization grant flow.
/// </summary>
public static class OAuthService
{
    private const string Authority = "https://localhost:5001";
    private const string BrowserClientId = "godot-browser-client";
    private const string DeviceClientId = "godot-device-client";
    private const string RedirectUri = "http://localhost:8948/callback";
    private const string Scopes = "openid profile email";

    private static DiscoveryDocumentResponse? _discoveryCache;


    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            // WARNING: DangerousAcceptAnyServerCertificateValidator is for LOCAL DEVELOPMENT ONLY.
            // This trusts any TLS certificate, but your ASP.NET Core certificate should already be trusted
            // — never use in production.
            // ServerCertificateCustomValidationCallback =
            //     HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        return new HttpClient(handler);
    }

    private static void LogError(string message)
    {
        // Uses Godot's print when available; falls back to stderr for unit test context
        Console.Error.WriteLine($"[OAuthService] {message}");
    }

    /// <summary>
    /// Fetches (and caches) the OIDC discovery document from the IdentityServer.
    /// </summary>
    public static async Task<DiscoveryDocumentResponse?> GetDiscoveryDocumentAsync()
    {
        if (_discoveryCache is { IsError: false })
            return _discoveryCache;

        using var client = CreateHttpClient();
        var disco = await client.GetDiscoveryDocumentAsync(Authority);

        if (disco.IsError)
        {
            LogError($"Discovery error: {disco.Error}");
            return null;
        }

        _discoveryCache = disco;
        return disco;
    }

    /// <summary>
    /// Generates a PKCE code verifier and Base64Url-encoded SHA-256 challenge.
    /// Returns (codeVerifier, codeChallenge).
    /// </summary>
    public static (string codeVerifier, string codeChallenge) GeneratePkce()
    {
        var verifier = CryptoRandom.CreateUniqueId(32);
        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Convert.ToBase64String(challengeBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        return (verifier, challenge);
    }

    /// <summary>
    /// Builds the OIDC authorization URL for the browser-based PKCE flow.
    /// </summary>
    public static async Task<string?> BuildAuthorizeUrl(string codeChallenge, string state)
    {
        var disco = await GetDiscoveryDocumentAsync();
        if (disco == null) return null;

        var request = new RequestUrl(disco.AuthorizeEndpoint!);
        return request.CreateAuthorizeUrl(
            clientId: BrowserClientId,
            responseType: OidcConstants.ResponseTypes.Code,
            scope: Scopes,
            redirectUri: RedirectUri,
            state: state,
            codeChallenge: codeChallenge,
            codeChallengeMethod: OidcConstants.CodeChallengeMethods.Sha256
        );
    }

    /// <summary>
    /// Exchanges an authorization code for tokens (Authorization Code + PKCE flow).
    /// Stores result in <see cref="TokenStorage"/> on success.
    /// Returns true on success.
    /// </summary>
    public static async Task<bool> ExchangeCodeForTokensAsync(string code, string codeVerifier)
    {
        var disco = await GetDiscoveryDocumentAsync();
        if (disco == null) return false;

        using var client = CreateHttpClient();
        var tokenResponse = await client.RequestAuthorizationCodeTokenAsync(
            new AuthorizationCodeTokenRequest
            {
                Address = disco.TokenEndpoint,
                ClientId = BrowserClientId,
                Code = code,
                RedirectUri = RedirectUri,
                CodeVerifier = codeVerifier
            });

        if (tokenResponse.IsError)
        {
            LogError($"Token exchange error: {tokenResponse.Error} — {tokenResponse.ErrorDescription}");
            return false;
        }

        StoreTokens(tokenResponse);
        return true;
    }

    /// <summary>
    /// Requests device authorization from the IdentityServer (Device Authorization Grant).
    /// Returns the <see cref="DeviceAuthorizationResponse"/> containing user_code and verification_uri.
    /// </summary>
    public static async Task<DeviceAuthorizationResponse?> RequestDeviceAuthorizationAsync()
    {
        var disco = await GetDiscoveryDocumentAsync();
        if (disco == null) return null;

        using var client = CreateHttpClient();
        var response = await client.RequestDeviceAuthorizationAsync(
            new DeviceAuthorizationRequest
            {
                Address = disco.DeviceAuthorizationEndpoint,
                ClientId = DeviceClientId,
                Scope = Scopes
            });

        if (response.IsError)
        {
            LogError($"Device auth error: {response.Error} — {response.ErrorDescription}");
            return null;
        }

        return response;
    }

    /// <summary>
    /// Polls the token endpoint once for a device token.
    /// Returns a <see cref="DevicePollResult"/> indicating the outcome.
    /// On <see cref="DevicePollResult.Success"/>, tokens are stored in <see cref="TokenStorage"/>.
    /// </summary>
    public static async Task<DevicePollResult> PollForDeviceTokenAsync(string deviceCode)
    {
        var disco = await GetDiscoveryDocumentAsync();
        if (disco == null) return DevicePollResult.Error;

        using var client = CreateHttpClient();
        var tokenResponse = await client.RequestDeviceTokenAsync(
            new DeviceTokenRequest
            {
                Address = disco.TokenEndpoint,
                ClientId = DeviceClientId,
                DeviceCode = deviceCode
            });

        if (!tokenResponse.IsError)
        {
            StoreTokens(tokenResponse);
            return DevicePollResult.Success;
        }

        if (tokenResponse.Error == OidcConstants.TokenErrors.AuthorizationPending)
            return DevicePollResult.Pending;
        if (tokenResponse.Error == OidcConstants.TokenErrors.SlowDown)
            return DevicePollResult.SlowDown;

        LogError($"Device poll error: {tokenResponse.Error} — {tokenResponse.ErrorDescription}");
        return DevicePollResult.Error;
    }

    /// <summary>
    /// Decodes the JWT ID token payload (base64url) and extracts claims into
    /// <see cref="TokenStorage.UserClaims"/>. No signature validation — dev sample only.
    /// </summary>
    public static void ParseIdTokenClaims(string idToken)
    {
        try
        {
            var parts = idToken.Split('.');
            if (parts.Length < 2) return;

            // Base64Url-decode the payload segment
            var payload = parts[1];

            // Re-pad the Base64Url string to standard Base64
            var remainder = payload.Length % 4;
            if (remainder == 2) payload += "==";
            else if (remainder == 3) payload += "=";

            payload = payload.Replace('-', '+').Replace('_', '/');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));

            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                TokenStorage.UserClaims[prop.Name] = prop.Value.ToString();
            }
        }
        catch (Exception ex)
        {
            LogError($"ID token parse error: {ex.Message}");
        }
    }

    // --- Private helpers ---

    /// <summary>
    /// Calls the UserInfo endpoint with the access token to retrieve profile claims
    /// (name, email, etc.) and merges them into <see cref="TokenStorage.UserClaims"/>.
    /// </summary>
    public static async Task FetchUserInfoAsync()
    {
        var disco = await GetDiscoveryDocumentAsync();
        if (disco == null) return;

        if (string.IsNullOrEmpty(TokenStorage.AccessToken))
        {
            LogError("Cannot fetch UserInfo — no access token.");
            return;
        }

        using var client = CreateHttpClient();
        var response = await client.GetUserInfoAsync(new UserInfoRequest
        {
            Address = disco.UserInfoEndpoint,
            Token = TokenStorage.AccessToken
        });

        if (response.IsError)
        {
            LogError($"UserInfo error: {response.Error}");
            return;
        }

        // Merge UserInfo claims into storage (overwrites any existing values)
        foreach (var claim in response.Claims)
        {
            TokenStorage.UserClaims[claim.Type] = claim.Value;
        }
    }

    private static void StoreTokens(TokenResponse tokenResponse)
    {
        TokenStorage.AccessToken = tokenResponse.AccessToken;
        TokenStorage.RefreshToken = tokenResponse.RefreshToken;
        TokenStorage.IdToken = tokenResponse.IdentityToken;

        if (!string.IsNullOrEmpty(tokenResponse.IdentityToken))
            ParseIdTokenClaims(tokenResponse.IdentityToken);
    }
}
