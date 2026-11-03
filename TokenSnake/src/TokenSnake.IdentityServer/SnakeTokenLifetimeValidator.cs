using Duende.IdentityServer.Validation;
using TokenSnake.Game;

namespace TokenSnake.IdentityServer;

/// <summary>Difficulty lives here: the higher the level, the shorter the new access token lives.</summary>
public sealed class SnakeTokenLifetimeValidator(IGameStateStore store) : ICustomTokenRequestValidator
{
    public Task ValidateAsync(CustomTokenRequestValidationContext context, CancellationToken ct)
    {
        var request = context.Result?.ValidatedRequest;
        if (request?.GrantType != SnakeNames.EatGrantType) return Task.CompletedTask;

        var gameId = request.Subject?.FindFirst(SnakeNames.GameId)?.Value;
        var state = gameId is null ? null : store.Get(gameId);
        if (state is not null)
            request.AccessTokenLifetime = LevelRules.TokenLifetimeSecondsFor(state.Level);

        return Task.CompletedTask;
    }
}
