using System.Globalization;
using System.Security.Claims;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using TokenSnake.Game;

namespace TokenSnake.IdentityServer;

/// <summary>
/// There is no user store. The player's name comes from the subject's claims, and the
/// game claims come from the server-side game state, never from the client.
/// </summary>
public sealed class SnakeProfileService(IGameStateStore store) : IProfileService
{
    public Task GetProfileDataAsync(ProfileDataRequestContext context, CancellationToken ct)
    {
        var subject = context.Subject;
        var sub = subject.FindFirst("sub")?.Value ?? "";
        var nickname = subject.FindFirst(SnakeNames.Nickname)?.Value ?? Nickname.FromSubject(sub);

        var claims = new List<Claim> { new("name", nickname), new(SnakeNames.Nickname, nickname) };
        claims.AddRange(GameClaims(subject, sub));

        // Only the claims the requested scopes ask for end up in the token.
        context.AddRequestedClaims(claims);
        return Task.CompletedTask;
    }

    // Any well-formed sub is "active": a logged-in nickname has no account to disable.
    public Task IsActiveAsync(IsActiveContext context, CancellationToken ct)
    {
        context.IsActive = context.Subject.FindFirst("sub")?.Value.Contains(':') == true;
        return Task.CompletedTask;
    }

    private IEnumerable<Claim> GameClaims(ClaimsPrincipal subject, string sub)
    {
        var gameId = subject.FindFirst(SnakeNames.GameId)?.Value;
        var state = gameId is null ? null : store.Get(gameId);
        if (state is null || state.Subject != sub) yield break;

        yield return new Claim(SnakeNames.GameId, state.GameId);
        yield return Number(SnakeNames.Score, state.Score);
        yield return Number(SnakeNames.Length, state.Length);
        yield return Number(SnakeNames.Level, state.Level);
        yield return Number(SnakeNames.Pellets, state.Pellets);
        yield return new Claim(SnakeNames.PathHash, state.PathHash);
    }

    // Integer value types make them JSON numbers in the JWT instead of strings.
    private static Claim Number(string type, int value) =>
        new(type, value.ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer32);
}
