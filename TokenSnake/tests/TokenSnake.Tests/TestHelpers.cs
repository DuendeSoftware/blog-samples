using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Duende.IdentityServer;
using Duende.IdentityServer.Configuration;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using TokenSnake.Game;
using TokenSnake.IdentityServer;
using TokenSnake.Web;
using Xunit;

namespace TokenSnake.Tests;

internal static class T
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;
    public const string Sub = "Ada:0123456789abcdef0123456789abcdef";
    public static readonly (Direction D, char C)[] Dirs =
        [(Direction.Up, 'U'), (Direction.Down, 'D'), (Direction.Left, 'L'), (Direction.Right, 'R')];

    public static JsonElement Claims(string jwt) =>
        JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(jwt.Split('.')[1])).RootElement.Clone();

    public static GameState NewState(int seedByte = 1, long seq = 0, DateTimeOffset? now = null) =>
        GameState.Start("g" + seedByte, Sub, Enumerable.Repeat((byte)seedByte, 32).ToArray(), GameRules.Default,
            now ?? DateTimeOffset.UnixEpoch);

    /// <summary>Breadth-first search for a legal path from the state's head to its current pellet.</summary>
    public static string PathToPellet(GameState state, GameRules? rules = null)
    {
        rules ??= GameRules.Default;
        var pellet = state.CurrentPellet(rules);
        var queue = new Queue<(Cell, Direction, string)>();
        var seen = new HashSet<(Cell, Direction)>();
        queue.Enqueue((state.Body[0], state.Direction, ""));
        while (queue.Count > 0)
        {
            var (cell, dir, path) = queue.Dequeue();
            foreach (var (d, ch) in Dirs)
            {
                if (d == GameRules.Opposite(dir)) continue;
                var next = cell.Step(d);
                if (!rules.Contains(next) || !seen.Add((next, d))) continue;
                if (next == pellet && PathReplayEngine.Replay(state.Body, state.Direction, path + ch, rules, pellet).Ok)
                    return path + ch;
                queue.Enqueue((next, d, path + ch));
            }
        }
        throw new InvalidOperationException("no path");
    }

    /// <summary>A path straight into a wall that never crosses the pellet, so the outcome is deterministic.</summary>
    public static string WallPath(GameState state, GameRules? rules = null)
    {
        rules ??= GameRules.Default;
        var pellet = state.CurrentPellet(rules);
        foreach (var (d, ch) in Dirs)
        {
            if (d == GameRules.Opposite(state.Direction)) continue;
            var cell = state.Body[0];
            var clear = true;
            for (var i = 0; i < rules.Width + rules.Height; i++)
            {
                cell = cell.Step(d);
                if (cell == pellet) { clear = false; break; }
            }
            if (clear) return new string(ch, rules.Width + rules.Height);
        }
        throw new InvalidOperationException("no clear wall path");
    }

    /// <summary>Finds a seed byte whose first pellet satisfies the predicate (deterministic search).</summary>
    public static GameState FindState(Func<GameState, bool> predicate)
    {
        for (var i = 1; i < 255; i++)
            if (NewState(i) is var s && predicate(s)) return s;
        throw new InvalidOperationException("no suitable seed");
    }

    public static string Ids(this IEnumerable<char> c) => new(c.ToArray());
}

/// <summary>In-process IdentityServer with a fake clock and a fixed issuer.</summary>
internal sealed class IdentityEnv : IDisposable
{
    public FakeTimeProvider Time { get; }
    public WebApplicationFactory<SnakeEatGrantValidator> Factory { get; }
    public HttpClient Http { get; }
    public IServiceProvider Sp => Factory.Services;
    public IGameStateStore Store => Sp.GetRequiredService<IGameStateStore>();

    public IdentityEnv(DateTimeOffset? start = null, Action<IServiceCollection>? configure = null)
    {
        Time = new FakeTimeProvider(start ?? DateTimeOffset.UtcNow);
        Factory = new WebApplicationFactory<SnakeEatGrantValidator>().WithWebHostBuilder(b =>
        {
            b.UseSetting("TokenSnake:GameOrigin", "https://localhost:5002");
            b.ConfigureServices(s =>
            {
                s.AddSingleton<TimeProvider>(Time);
                // Minting outside a request needs a fixed issuer.
                s.Configure<IdentityServerOptions>(o => o.IssuerUri = "https://localhost:5001");
                configure?.Invoke(s);
            });
        });
        Http = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public Task<string> Mint(int lifetime = 300, string clientId = SnakeNames.ClientId, string sub = T.Sub, string aud = "snake.api") =>
        Sp.GetRequiredService<IIdentityServerTools>().IssueJwtAsync(lifetime,
        [
            new Claim("sub", sub), new Claim("client_id", clientId), new Claim("scope", "snake.play"), new Claim("aud", aud),
        ], T.Ct);

    public static (string, string)[] Start(string token) =>
        [("grant_type", "urn:snake:eat"), ("client_id", "snake.web"), ("scope", "snake.play"), ("subject_token", token), ("action", "start")];

    public static (string, string)[] Eat(string token, string gameId, long seq, int pelletId, string moves) =>
        [("grant_type", "urn:snake:eat"), ("client_id", "snake.web"), ("scope", "snake.play"), ("subject_token", token),
         ("action", "eat"), ("game_id", gameId), ("seq", seq.ToString()), ("pellet_id", pelletId.ToString()), ("moves", moves)];

    public static (string, string)[] Grant(string token, params (string, string)[] extra) =>
        [("grant_type", "urn:snake:eat"), ("client_id", "snake.web"), ("scope", "snake.play"), ("subject_token", token), .. extra];

    public async Task<(HttpStatusCode Status, JsonElement Json)> Post(params (string, string)[] form)
    {
        var res = await Http.PostAsync("/connect/token",
            new FormUrlEncodedContent(form.Select(p => KeyValuePair.Create(p.Item1, p.Item2))), T.Ct);
        var body = await res.Content.ReadAsStringAsync(T.Ct);
        return (res.StatusCode, JsonDocument.Parse(body).RootElement.Clone());
    }

    /// <summary>Starts a game from a fresh T0. Returns (T1, gameId, response).</summary>
    public async Task<(string T1, string GameId, JsonElement Json)> StartGame(string? t0 = null)
    {
        var (status, json) = await Post(Start(t0 ?? await Mint()));
        Assert.Equal(HttpStatusCode.OK, status);
        return (json.GetProperty("access_token").GetString()!, json.GetProperty("game_id").GetString()!, json);
    }

    /// <summary>Starts a game and eats the first pellet. Returns (T1 score 0, T2 score 10).</summary>
    public async Task<(string T1, string T2, string GameId)> PlayOnePellet()
    {
        var (t1, gameId, _) = await StartGame();
        var moves = T.PathToPellet(Store.Get(gameId)!);
        Time.Advance(TimeSpan.FromMilliseconds(250 * moves.Length));
        var (status, eat) = await Post(Eat(t1, gameId, 1, 0, moves));
        Assert.Equal(HttpStatusCode.OK, status);
        return (t1, eat.GetProperty("access_token").GetString()!, gameId);
    }

    /// <summary>Runs the real authorization-code + PKCE flow as a fresh browser, returning the T0 access token.</summary>
    public async Task<string> LoginForT0(string nickname)
    {
        using var http = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var verifier = new string('a', 64);
        var challenge = Base64UrlEncoder.Encode(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));
        var authorize = "/connect/authorize?client_id=snake.web&response_type=code&scope=openid%20profile%20snake.play" +
            "&redirect_uri=" + Uri.EscapeDataString("https://localhost:5002/") +
            "&code_challenge=" + challenge + "&code_challenge_method=S256&state=xyz";

        var r1 = await http.GetAsync(authorize, T.Ct);
        var loginUrl = r1.Headers.Location!.ToString();
        var (token, returnUrl) = await ReadLoginForm(http, loginUrl);
        var ok = await http.PostAsync(loginUrl, LoginForm(nickname, returnUrl, token), T.Ct);
        Assert.True((int)ok.StatusCode is 302 or 303);

        var r2 = await http.GetAsync(ok.Headers.Location, T.Ct);
        var code = System.Web.HttpUtility.ParseQueryString(r2.Headers.Location!.Query)["code"]!;
        var (st, tokens) = await Post(("grant_type", "authorization_code"), ("client_id", "snake.web"), ("code", code),
            ("redirect_uri", "https://localhost:5002/"), ("code_verifier", verifier));
        Assert.Equal(HttpStatusCode.OK, st);
        return tokens.GetProperty("access_token").GetString()!;
    }

    public static async Task<(string Token, string ReturnUrl)> ReadLoginForm(HttpClient http, string loginUrl)
    {
        var page = await http.GetStringAsync(loginUrl, T.Ct);
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var returnUrl = WebUtility.HtmlDecode(Regex.Match(page, "name=\"ReturnUrl\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
        return (token, returnUrl);
    }

    public static FormUrlEncodedContent LoginForm(string nickname, string returnUrl, string antiforgery) =>
        new(new Dictionary<string, string>
            { ["Nickname"] = nickname, ["ReturnUrl"] = returnUrl, ["__RequestVerificationToken"] = antiforgery });

    public void Dispose() { Http.Dispose(); Factory.Dispose(); }
}

/// <summary>The Web host, with JWT discovery/JWKS routed to an in-memory IdentityServer.</summary>
internal sealed class WebEnv : IDisposable
{
    public IdentityEnv Identity { get; }
    public WebApplicationFactory<WebMarker> Factory { get; }
    public HttpClient Http { get; }

    public WebEnv(DateTimeOffset? identityNow = null)
    {
        Identity = new IdentityEnv(identityNow);
        Factory = new WebApplicationFactory<WebMarker>().WithWebHostBuilder(b =>
        {
            b.UseSetting("TokenSnake:Authority", "https://localhost:5001");
            b.ConfigureServices(s => s.Configure<JwtBearerOptions>(LeaderboardApi.Scheme,
                o => o.BackchannelHttpHandler = Identity.Factory.Server.CreateHandler()));
        });
        Http = Factory.CreateClient();
    }

    public void Dispose() { Http.Dispose(); Factory.Dispose(); Identity.Dispose(); }
}
