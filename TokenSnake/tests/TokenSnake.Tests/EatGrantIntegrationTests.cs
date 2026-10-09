using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TokenSnake.Game;
using Xunit;

namespace TokenSnake.Tests;

public class EatGrantIntegrationTests
{
    [Fact]
    public async Task Discovery_lists_grant_and_cors_is_origin_scoped()
    {
        using var e = new IdentityEnv();
        Assert.Contains("urn:snake:eat", await e.Http.GetStringAsync("/.well-known/openid-configuration", T.Ct));

        foreach (var (origin, allowed) in new[] { ("https://localhost:5002", true), ("https://evil.example", false) })
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/connect/token") { Content = new FormUrlEncodedContent([]) };
            req.Headers.Add("Origin", origin);
            var res = await e.Http.SendAsync(req, T.Ct);
            var acao = res.Headers.TryGetValues("Access-Control-Allow-Origin", out var v) ? string.Join(",", v) : null;
            Assert.Equal(allowed ? origin : null, acao);
        }
    }

    [Fact]
    public async Task Start_returns_deterministic_start_state_and_level_one_token()
    {
        using var e = new IdentityEnv();
        var (t1, gameId, json) = await e.StartGame();
        var rules = json.GetProperty("rules");
        Assert.Equal(20, rules.GetProperty("width").GetInt32());
        Assert.Equal(20, rules.GetProperty("height").GetInt32());
        Assert.Equal(10, rules.GetProperty("start_x").GetInt32());
        Assert.Equal(10, rules.GetProperty("start_y").GetInt32());
        Assert.Equal("right", rules.GetProperty("start_direction").GetString());
        Assert.Equal(3, rules.GetProperty("start_length").GetInt32());
        Assert.Equal(0, json.GetProperty("pellet_id").GetInt32());
        Assert.Equal(200, json.GetProperty("tick_ms").GetInt32());
        Assert.False(json.TryGetProperty("refresh_token", out _));

        var state = e.Store.Get(gameId)!;
        Assert.Equal(GameRules.Default.CreateStartBody(), state.Body);
        var pellet = state.CurrentPellet(GameRules.Default);
        Assert.Equal(pellet.X, json.GetProperty("next_pellet").GetProperty("x").GetInt32());
        Assert.Equal(pellet.Y, json.GetProperty("next_pellet").GetProperty("y").GetInt32());

        var c = T.Claims(t1);
        Assert.Equal(0, c.GetProperty("score").GetInt32());
        Assert.Equal(3, c.GetProperty("length").GetInt32());
        Assert.Equal(60, c.GetProperty("exp").GetInt64() - c.GetProperty("iat").GetInt64());
        Assert.Equal(gameId, c.GetProperty("game_id").GetString());
    }

    [Fact]
    public async Task Eat_issues_new_token_with_score_level_exp_and_changed_length_and_path_hash()
    {
        using var e = new IdentityEnv();
        var (t1, gameId, _) = await e.StartGame();
        var c1 = T.Claims(t1);

        var moves = T.PathToPellet(e.Store.Get(gameId)!);
        e.Time.Advance(TimeSpan.FromMilliseconds(250 * moves.Length));
        var (status, eat) = await e.Post(IdentityEnv.Eat(t1, gameId, 1, 0, moves));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(eat.TryGetProperty("refresh_token", out _));
        var t2 = eat.GetProperty("access_token").GetString()!;
        var c2 = T.Claims(t2);

        Assert.Equal(10, c2.GetProperty("score").GetInt32());
        Assert.Equal(4, c2.GetProperty("length").GetInt32());
        Assert.Equal(60, c2.GetProperty("exp").GetInt64() - c2.GetProperty("iat").GetInt64()); // level 1
        Assert.NotEqual(c1.GetProperty("jti").GetString(), c2.GetProperty("jti").GetString());
        Assert.NotEqual(c1.GetProperty("path_hash").GetString(), c2.GetProperty("path_hash").GetString());
        Assert.NotEqual(c1.GetProperty("length").GetInt32(), c2.GetProperty("length").GetInt32());
        Assert.Equal("Ada", c2.GetProperty("nickname").GetString());

        // Second eat: hash and length change again.
        var moves2 = T.PathToPellet(e.Store.Get(gameId)!);
        e.Time.Advance(TimeSpan.FromMilliseconds(250 * moves2.Length));
        var (s3, eat2) = await e.Post(IdentityEnv.Eat(t2, gameId, 2, 1, moves2));
        Assert.Equal(HttpStatusCode.OK, s3);
        var c3 = T.Claims(eat2.GetProperty("access_token").GetString()!);
        Assert.NotEqual(c2.GetProperty("path_hash").GetString(), c3.GetProperty("path_hash").GetString());
        Assert.Equal(5, c3.GetProperty("length").GetInt32());
        Assert.Equal(20, c3.GetProperty("score").GetInt32());
    }

    [Fact]
    public async Task Higher_level_gets_shorter_token_lifetime()
    {
        using var e = new IdentityEnv();
        var (t, gameId, _) = await e.StartGame();
        // Fast-forward server state to level 2 (5 pellets) without playing them out.
        var s = e.Store.Get(gameId)!;
        Assert.True(e.Store.TryUpdate(s with { Pellets = 5 }, s.Seq));
        var moves = T.PathToPellet(e.Store.Get(gameId)!);
        e.Time.Advance(TimeSpan.FromSeconds(10));
        var (status, eat) = await e.Post(IdentityEnv.Eat(t, gameId, 1, 5, moves));
        Assert.Equal(HttpStatusCode.OK, status);
        var c = T.Claims(eat.GetProperty("access_token").GetString()!);
        Assert.Equal(LevelRules.TokenLifetimeSecondsFor(2), c.GetProperty("exp").GetInt64() - c.GetProperty("iat").GetInt64());
        Assert.Equal(20, c.GetProperty("score").GetInt32()); // level 2 pays 20 points
        Assert.Equal(2, c.GetProperty("level").GetInt32());
    }

    [Fact]
    public async Task Expired_subject_token_returns_token_expired_and_ends_game()
    {
        using var e = new IdentityEnv();
        var (t1, gameId, _) = await e.StartGame();
        e.Time.Advance(TimeSpan.FromSeconds(120));
        var (_, res) = await e.Post(IdentityEnv.Eat(t1, gameId, 1, 0, "R"));
        Assert.Equal("invalid_grant", res.GetProperty("error").GetString());
        Assert.Equal("token_expired", res.GetProperty("error_description").GetString());
        Assert.Equal(GameStatus.Over, e.Store.Get(gameId)!.Status);
    }

    [Fact]
    public async Task Token_expired_beyond_identityserver_skew_is_rejected()
    {
        using var e = new IdentityEnv();
        var old = await e.Mint(10);
        e.Time.Advance(TimeSpan.FromMinutes(10));
        var (_, res) = await e.Post(IdentityEnv.Start(old));
        Assert.Equal("invalid_grant", res.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Token_issued_to_another_client_is_rejected()
    {
        using var e = new IdentityEnv();
        var (_, res) = await e.Post(IdentityEnv.Start(await e.Mint(clientId: "someone.else")));
        Assert.Equal("invalid_grant", res.GetProperty("error").GetString());
        Assert.Equal("invalid_subject_token", res.GetProperty("error_description").GetString());
    }

    [Fact]
    public async Task Wall_collision_ends_the_game_deterministically()
    {
        using var e = new IdentityEnv();
        var (t1, gameId, _) = await e.StartGame();
        e.Time.Advance(TimeSpan.FromSeconds(5));
        var (_, over) = await e.Post(IdentityEnv.Eat(t1, gameId, 1, 0, T.WallPath(e.Store.Get(gameId)!)));
        Assert.Equal("collision", over.GetProperty("error_description").GetString());
        Assert.Equal(GameStatus.Over, e.Store.Get(gameId)!.Status);
    }

    [Fact]
    public async Task Self_colliding_path_returns_collision_and_ends_the_game()
    {
        using var e = new IdentityEnv();
        var (t1, gameId, _) = await e.StartGame();

        // Give the server-side snake a long body so U, L, D bites it, and pick a seed whose pellet is off that path.
        var rules = GameRules.Default;
        var body = Enumerable.Range(0, 6).Select(i => new Cell(10 - i, 10)).ToList();
        var s = e.Store.Get(gameId)!;
        var forbidden = new[] { new Cell(10, 9), new Cell(9, 9), new Cell(9, 10) };
        GameState bent = s;
        for (var i = 1; i < 255; i++)
        {
            bent = s with { Body = body, Seed = Enumerable.Repeat((byte)i, 32).ToArray() };
            if (!forbidden.Contains(bent.CurrentPellet(rules))) break;
        }
        Assert.True(e.Store.TryUpdate(bent, s.Seq));

        e.Time.Advance(TimeSpan.FromSeconds(5));
        var (_, over) = await e.Post(IdentityEnv.Eat(t1, gameId, 1, 0, "ULDL"));
        Assert.Equal("collision", over.GetProperty("error_description").GetString());
        Assert.Equal(GameStatus.Over, e.Store.Get(gameId)!.Status);

        // Over is final: even a perfectly valid follow-up is refused.
        var (status, again) = await e.Post(IdentityEnv.Eat(t1, gameId, 1, 0, "R"));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("game_not_active", again.GetProperty("error_description").GetString());
    }

    [Fact]
    public async Task Unknown_game_and_bad_action_are_rejected()
    {
        using var e = new IdentityEnv();
        var (t1, _, _) = await e.StartGame();
        var (_, unknown) = await e.Post(IdentityEnv.Eat(t1, "nope", 1, 0, "R"));
        Assert.Equal("unknown_game", unknown.GetProperty("error_description").GetString());
        var (_, bad) = await e.Post(IdentityEnv.Grant(t1, ("action", "dance")));
        Assert.Equal("bad_action", bad.GetProperty("error_description").GetString());
    }

    [Fact]
    public async Task Eat_with_an_older_token_is_rejected_without_ending_the_game()
    {
        using var e = new IdentityEnv();
        var (t1, gameId, _) = await e.StartGame();
        var moves = T.PathToPellet(e.Store.Get(gameId)!);
        e.Time.Advance(TimeSpan.FromMilliseconds(250 * moves.Length));
        var (s, eat) = await e.Post(IdentityEnv.Eat(t1, gameId, 1, 0, moves));
        Assert.Equal(HttpStatusCode.OK, s);

        var moves2 = T.PathToPellet(e.Store.Get(gameId)!);
        e.Time.Advance(TimeSpan.FromMilliseconds(250 * moves2.Length));
        var (status, stale) = await e.Post(IdentityEnv.Eat(t1, gameId, 2, 1, moves2)); // t1 is older than the newest token
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("stale_token", stale.GetProperty("error_description").GetString());
        Assert.Equal(GameStatus.Active, e.Store.Get(gameId)!.Status);
        Assert.Equal(1, e.Store.Get(gameId)!.Seq);

        var t2 = eat.GetProperty("access_token").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await e.Post(IdentityEnv.Eat(t2, gameId, 2, 1, moves2))).Status);
    }
}
