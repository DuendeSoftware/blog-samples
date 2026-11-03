using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace TokenSnake.Tests;

public class LoginIntegrationTests
{
    private const string Authorize =
        "/connect/authorize?client_id=snake.web&response_type=code&scope=openid%20profile%20snake.play" +
        "&redirect_uri=https%3A%2F%2Flocalhost%3A5002%2F" +
        "&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256&state=xyz";

    private static async Task<(string LoginUrl, string Token, string ReturnUrl)> OpenLogin(HttpClient http)
    {
        var r = await http.GetAsync(Authorize, T.Ct);
        var loginUrl = r.Headers.Location!.ToString();
        var (token, returnUrl) = await IdentityEnv.ReadLoginForm(http, loginUrl);
        return (loginUrl, token, returnUrl);
    }

    [Fact]
    public async Task Invalid_nickname_rerenders_with_error_and_no_cookie()
    {
        using var e = new IdentityEnv();
        var (url, token, returnUrl) = await OpenLogin(e.Http);

        var bad = await e.Http.PostAsync(url, IdentityEnv.LoginForm("<b>", returnUrl, token), T.Ct);
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        Assert.Contains("Use only letters", await bad.Content.ReadAsStringAsync(T.Ct));
        var cookies = bad.Headers.TryGetValues("Set-Cookie", out var sc) ? string.Join(";", sc) : "";
        Assert.DoesNotContain("idsrv=", cookies);

        var blocked = await e.Http.PostAsync(url, IdentityEnv.LoginForm("MrAdmin", returnUrl, token), T.Ct);
        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);
        Assert.Contains("isn't available", WebUtility.HtmlDecode(await blocked.Content.ReadAsStringAsync(T.Ct)));
    }

    [Fact]
    public async Task Open_redirect_return_url_is_refused()
    {
        using var e = new IdentityEnv();
        var (url, token, _) = await OpenLogin(e.Http);
        var evil = await e.Http.PostAsync(url, IdentityEnv.LoginForm("Ada", "https://evil.example/", token), T.Ct);
        Assert.Equal(HttpStatusCode.OK, evil.StatusCode);
        var cookies = evil.Headers.TryGetValues("Set-Cookie", out var sc) ? string.Join(";", sc) : "";
        Assert.DoesNotContain("idsrv=", cookies);
    }

    [Fact]
    public async Task Login_flow_issues_T0_with_normalized_nickname_and_well_formed_sub()
    {
        using var e = new IdentityEnv();
        var t0 = await e.LoginForT0("  Ada   L  ");
        var c = T.Claims(t0);
        Assert.Equal("Ada L", c.GetProperty("nickname").GetString());
        Assert.Matches(new Regex("^[A-Za-z0-9 _.-]{2,20}:[0-9a-f]{32}$"), c.GetProperty("sub").GetString()!);
        Assert.False(c.TryGetProperty("score", out _));

        // The T0 from the real flow starts a game.
        var (_, _, json) = await e.StartGame(t0);
        Assert.Equal(20, json.GetProperty("rules").GetProperty("width").GetInt32());
    }

    [Fact]
    public async Task Two_sign_ins_with_same_nickname_get_different_subs()
    {
        using var e = new IdentityEnv();
        var a = T.Claims(await e.LoginForT0("Ada")).GetProperty("sub").GetString();
        var b = T.Claims(await e.LoginForT0("Ada")).GetProperty("sub").GetString();
        Assert.NotEqual(a, b);
        Assert.StartsWith("Ada:", a);
        Assert.StartsWith("Ada:", b);
    }

    // --- sign out -------------------------------------------------------------------------

    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"; // RFC 7636 example, matches the challenge in Authorize;

    /// <summary>Signs in with a nickname and returns the id token (the end-session hint). The client keeps the login cookie.</summary>
    private static async Task<string> SignIn(IdentityEnv e, string nickname)
    {
        var (url, token, returnUrl) = await OpenLogin(e.Http);
        var ok = await e.Http.PostAsync(url, IdentityEnv.LoginForm(nickname, returnUrl, token), T.Ct);
        Assert.True((int)ok.StatusCode is 302 or 303);
        var callback = await e.Http.GetAsync(ok.Headers.Location, T.Ct);
        var code = System.Web.HttpUtility.ParseQueryString(callback.Headers.Location!.Query)["code"]!;
        var (status, tokens) = await e.Post(("grant_type", "authorization_code"), ("client_id", "snake.web"), ("code", code),
            ("redirect_uri", "https://localhost:5002/"), ("code_verifier", Verifier));
        Assert.Equal(HttpStatusCode.OK, status);
        return tokens.GetProperty("id_token").GetString()!;
    }

    private static string SignOutUrl(string idToken, string redirect = "https://localhost:5002/") =>
        "/connect/endsession?id_token_hint=" + Uri.EscapeDataString(idToken) +
        "&post_logout_redirect_uri=" + Uri.EscapeDataString(redirect);

    [Fact]
    public async Task Sign_out_ends_the_session_so_the_next_authorize_shows_the_login_page()
    {
        using var e = new IdentityEnv();
        // The PKCE challenge in Authorize is for this verifier, so reuse it.
        var idToken = await SignIn(e, "Ada");

        // Still signed in: authorize goes straight back to the game with a code (no login page).
        var again = await e.Http.GetAsync(Authorize, T.Ct);
        Assert.StartsWith("https://localhost:5002/", again.Headers.Location!.ToString());

        var endSession = await e.Http.GetAsync(SignOutUrl(idToken), T.Ct);
        Assert.Contains("/Account/Logout", endSession.Headers.Location!.ToString());
        var logout = await e.Http.GetAsync(endSession.Headers.Location, T.Ct);
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode); // no confirmation prompt

        // Signed out: the same authorize request now needs the login page again.
        var next = await e.Http.GetAsync(Authorize, T.Ct);
        Assert.Contains("/Account/Login", next.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Sign_out_redirects_to_the_registered_post_logout_uri()
    {
        using var e = new IdentityEnv();
        var idToken = await SignIn(e, "Grace");

        var endSession = await e.Http.GetAsync(SignOutUrl(idToken), T.Ct);
        var logout = await e.Http.GetAsync(endSession.Headers.Location, T.Ct);
        Assert.Equal("https://localhost:5002/", logout.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Sign_out_ignores_a_post_logout_uri_that_is_not_registered()
    {
        using var e = new IdentityEnv();
        var idToken = await SignIn(e, "Linus");

        var endSession = await e.Http.GetAsync(SignOutUrl(idToken, "https://evil.example/"), T.Ct);
        var logout = await e.Http.GetAsync(endSession.Headers.Location, T.Ct);
        Assert.DoesNotContain("evil.example", logout.Headers.Location?.ToString() ?? "");
    }
}
