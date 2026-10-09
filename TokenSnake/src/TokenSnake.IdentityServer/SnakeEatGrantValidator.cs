using System.Collections.Specialized;
using System.Security.Claims;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Validation;
using TokenSnake.Game;

namespace TokenSnake.IdentityServer;

/// <summary>
/// The heart of the game. grant_type=urn:snake:eat swaps a valid token for a new one:
/// <c>action=start</c> creates a game, <c>action=eat</c> proves the path to the next pellet.
/// </summary>
public sealed class SnakeEatGrantValidator(
    ITokenValidator tokenValidator,
    IGameStateStore store,
    MoveValidator moveValidator,
    GameRules rules,
    TimeProvider time) : IExtensionGrantValidator
{
    // Far above any legitimate JWT or move string; this is a cheap guard before real work.
    private const int MaxSubjectTokenLength = 4096;

    public string GrantType => SnakeNames.EatGrantType;

    public async Task ValidateAsync(ExtensionGrantValidationContext context, CancellationToken ct)
    {
        var raw = context.Request.Raw;

        // Tokens are only accepted from the game's own client.
        if (context.Request.Client.ClientId != SnakeNames.ClientId)
        {
            context.Result = Error("invalid_client");
            return;
        }

        var subjectToken = raw["subject_token"];
        if (string.IsNullOrEmpty(subjectToken) || subjectToken.Length > MaxSubjectTokenLength)
        {
            context.Result = Error("missing_subject_token");
            return;
        }

        var token = await ReadSubjectTokenAsync(subjectToken, ct);
        if (token.Error is not null)
        {
            context.Result = Error(token.Error);
            return;
        }

        // Duende tolerates 5 minutes of clock skew when validating JWTs. The game doesn't:
        // when the token is expired the game is over, right now.
        if (time.GetUtcNow() > token.Expiry)
        {
            EndGame(token, RejectReason.TokenExpired);
            context.Result = Error(RejectReason.TokenExpired);
            return;
        }

        context.Result = raw["action"] switch
        {
            "start" => Start(token),
            "eat" => Eat(token, raw),
            _ => Error("bad_action"),
        };
    }

    // --- action=start ------------------------------------------------------------------

    private GrantValidationResult Start(SubjectToken token)
    {
        var seed = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var state = GameState.Start(Guid.NewGuid().ToString("N"), token.Sub, seed, rules, time.GetUtcNow());
        store.Create(state);

        // The rules go to the browser once, so the client never hard-codes the board.
        var response = Response(state);
        response["rules"] = new
        {
            width = rules.Width,
            height = rules.Height,
            start_x = rules.StartX,
            start_y = rules.StartY,
            start_direction = rules.StartDirection.ToString().ToLowerInvariant(),
            start_length = rules.StartLength,
        };
        return Success(token, state, response);
    }

    // --- action=eat --------------------------------------------------------------------

    private GrantValidationResult Eat(SubjectToken token, NameValueCollection raw)
    {
        var gameId = raw["game_id"];
        var state = gameId is null ? null : store.Get(gameId);

        // The token, the request and the stored game must all name the same player and game.
        if (state is null || gameId != token.GameId || state.Subject != token.Sub)
            return Error("unknown_game");

        // Each eat must use the token issued by the previous step: an older, still-valid token is stale.
        // Rejected without touching the game, like any other stale request.
        if (token.PathHash != state.PathHash) return Error("stale_token");

        if (!long.TryParse(raw["seq"], out var seq)
            || !int.TryParse(raw["pellet_id"], out var pelletId))
            return Error("bad_input");

        var moves = raw["moves"] ?? "";

        var result = moveValidator.Validate(state, new EatEvent(seq, pelletId, moves), token.Expiry, time.GetUtcNow());

        // A stale request leaves the game alone. A cheat or a collision ends it.
        if (result.Outcome == EatOutcome.Rejected) return Error(result.Reason!);
        if (!store.TryUpdate(result.State, state.Seq)) return Error(RejectReason.BadSeq);
        if (result.Outcome == EatOutcome.GameOver) return Error(result.Reason!);

        return Success(token, result.State, Response(result.State));
    }

    // --- subject token -----------------------------------------------------------------

    /// <summary>The checks every request needs: a valid, unexpired token from our client.</summary>
    private async Task<SubjectToken> ReadSubjectTokenAsync(string jwt, CancellationToken ct)
    {
        var result = await tokenValidator.ValidateAccessTokenAsync(jwt, SnakeNames.ApiScope, ct);
        if (result.IsError) return SubjectToken.Failed("invalid_subject_token");

        var claims = (result.Claims ?? []).ToList();
        var sub = Find(claims, "sub");
        var clientId = Find(claims, "client_id");
        var expSeconds = Find(claims, "exp");

        // A token issued to another client is never accepted here.
        if (sub is null || clientId != SnakeNames.ClientId || !long.TryParse(expSeconds, out var exp))
            return SubjectToken.Failed("invalid_subject_token");

        return new SubjectToken(null, sub, Find(claims, SnakeNames.GameId), Find(claims, SnakeNames.PathHash), DateTimeOffset.FromUnixTimeSeconds(exp));
    }

    /// <summary>An expired token ends the game it belongs to (if it is still that player's active game).</summary>
    private void EndGame(SubjectToken token, string reason)
    {
        var state = token.GameId is null ? null : store.Get(token.GameId);
        if (state is null || state.Subject != token.Sub || state.Status != GameStatus.Active) return;

        store.TryUpdate(state with { Status = GameStatus.Over }, state.Seq);
    }

    private static string? Find(IEnumerable<Claim> claims, string type) =>
        claims.FirstOrDefault(c => c.Type == type)?.Value;

    private sealed record SubjectToken(string? Error, string Sub, string? GameId, string? PathHash, DateTimeOffset Expiry)
    {
        public static SubjectToken Failed(string error) => new(error, "", null, null, default);
    }

    // --- responses ---------------------------------------------------------------------

    private Dictionary<string, object> Response(GameState state)
    {
        var pellet = state.CurrentPellet(rules);
        return new Dictionary<string, object>
        {
            ["game_id"] = state.GameId,
            ["pellet_id"] = state.Pellets,
            ["next_pellet"] = new { x = pellet.X, y = pellet.Y },
            ["level"] = state.Level,
            ["tick_ms"] = LevelRules.TickMsFor(state.Level),
            ["seq"] = state.Seq,
        };
    }

    private static GrantValidationResult Success(SubjectToken token, GameState state, Dictionary<string, object> response)
    {
        // These claims reach the profile service, which reads the real state from the store.
        var claims = new[]
        {
            new Claim(SnakeNames.GameId, state.GameId),
            new Claim(SnakeNames.Nickname, Nickname.FromSubject(token.Sub)),
            new Claim("name", Nickname.FromSubject(token.Sub)),
        };
        return new GrantValidationResult(token.Sub, SnakeNames.EatGrantType, claims, customResponse: response);
    }

    private static GrantValidationResult Error(string description) =>
        new(TokenRequestErrors.InvalidGrant, description);
}
