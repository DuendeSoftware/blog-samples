using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace TokenSnake.Tests;

public class LeaderboardIntegrationTests
{
    private static Task<HttpResponseMessage> Submit(WebEnv e, string token, object? body = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/leaderboard") { Content = JsonContent.Create(body ?? new { }) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return e.Http.SendAsync(req, T.Ct);
    }

    private static async Task<JsonElement> Board(WebEnv e) =>
        JsonDocument.Parse(await e.Http.GetStringAsync("/api/leaderboard", T.Ct)).RootElement.Clone();

    private static string SignedByStranger(string aud)
    {
        using var rsa = RSA.Create(2048);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://localhost:5001", Audience = aud, Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["sub"] = T.Sub, ["scope"] = "snake.play", ["game_id"] = "g", ["score"] = 999 },
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256),
        });
    }

    [Fact]
    public async Task Forged_unsigned_wrong_audience_and_gameless_tokens_are_refused()
    {
        using var e = new WebEnv();
        Assert.Equal(HttpStatusCode.Unauthorized, (await e.Http.PostAsync("/api/leaderboard", null, T.Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Submit(e, SignedByStranger("snake.api"))).StatusCode);

        var unsigned = Base64UrlEncoder.Encode("{\"alg\":\"none\"}") + "." + Base64UrlEncoder.Encode(
            "{\"iss\":\"https://localhost:5001\",\"aud\":\"snake.api\",\"sub\":\"x\",\"scope\":\"snake.play\",\"game_id\":\"g\",\"score\":999}") + ".";
        Assert.Equal(HttpStatusCode.Unauthorized, (await Submit(e, unsigned)).StatusCode);

        // Genuinely signed by IdentityServer but meant for another API.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Submit(e, await e.Identity.Mint(aud: "other.api"))).StatusCode);
        // Genuine, right audience, but no game: authenticated yet refused.
        Assert.Equal(HttpStatusCode.Forbidden, (await Submit(e, await e.Identity.Mint())).StatusCode);

        Assert.Equal(0, (await Board(e)).GetArrayLength());
    }

    [Fact]
    public async Task Valid_token_creates_entry_and_older_token_does_not_lower_it()
    {
        using var e = new WebEnv();
        var (t1, t2, _) = await e.Identity.PlayOnePellet();

        // The body is ignored: the claimed 9999 must not matter.
        Assert.Equal(HttpStatusCode.OK, (await Submit(e, t2, new { score = 9999, nickname = "<script>" })).StatusCode);
        var board = await Board(e);
        Assert.Equal(1, board.GetArrayLength());
        Assert.Equal(10, board[0].GetProperty("score").GetInt32());
        Assert.Equal("Ada", board[0].GetProperty("nickname").GetString());

        Assert.Equal(HttpStatusCode.OK, (await Submit(e, t1)).StatusCode); // older token, score 0
        board = await Board(e);
        Assert.Equal(1, board.GetArrayLength());
        Assert.Equal(10, board[0].GetProperty("score").GetInt32());
    }

    [Fact]
    public async Task Token_expired_within_two_minutes_is_accepted_but_not_beyond()
    {
        // Expired about 30 s ago: inside the 2-minute grace.
        using var recent = new WebEnv(DateTimeOffset.UtcNow.AddSeconds(-90));
        var (_, t2, _) = await recent.Identity.PlayOnePellet();
        Assert.Equal(HttpStatusCode.OK, (await Submit(recent, t2)).StatusCode);

        // Expired about 3 minutes ago: outside it (IdentityServer itself tolerates 5 minutes when reading T0).
        using var old = new WebEnv(DateTimeOffset.UtcNow.AddMinutes(-4));
        var (_, old2, _) = await old.Identity.PlayOnePellet();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Submit(old, old2)).StatusCode);
    }

    [Fact]
    public async Task Config_json_and_csp_come_from_configuration()
    {
        using var e = new WebEnv();
        var res = await e.Http.GetAsync("/config.json", T.Ct);
        var cfg = JsonDocument.Parse(await res.Content.ReadAsStringAsync(T.Ct)).RootElement;
        Assert.Equal("https://localhost:5001/connect/token", cfg.GetProperty("token_endpoint").GetString());
        Assert.Equal("https://localhost:5002/", cfg.GetProperty("redirect_uri").GetString());
        Assert.Equal("default-src 'self'; connect-src 'self' https://localhost:5001; script-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'",
            res.Headers.GetValues("Content-Security-Policy").Single());
    }
}
