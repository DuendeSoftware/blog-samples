using Duende.IdentityServer;
using Duende.IdentityServer.Models;

namespace TokenSnake.IdentityServer;

/// <summary>Names shared by the grant, the lifetime validator and the profile service.</summary>
public static class SnakeNames
{
    public const string ClientId = "snake.web";
    public const string EatGrantType = "urn:snake:eat";
    public const string ApiScope = "snake.play";
    public const string ApiResource = "snake.api";

    // Claim types used for the game state and the player's display name.
    public const string GameId = "game_id";
    public const string Score = "score";
    public const string Length = "length";
    public const string Level = "level";
    public const string Pellets = "pellets";
    public const string PathHash = "path_hash";
    public const string Nickname = "nickname";
}

public static class Config
{
    public static IEnumerable<IdentityResource> IdentityResources =>
    [
        new IdentityResources.OpenId(),
        new IdentityResources.Profile(),
    ];

    public static IEnumerable<ApiScope> ApiScopes =>
    [
        new ApiScope(SnakeNames.ApiScope, "Play TokenSnake"),
    ];

    /// <summary>
    /// The API resource decides which claims land in the access token.
    /// The game claims are filled in by <see cref="SnakeProfileService"/>.
    /// </summary>
    public static IEnumerable<ApiResource> ApiResources =>
    [
        new ApiResource(SnakeNames.ApiResource, "TokenSnake API")
        {
            Scopes = { SnakeNames.ApiScope },
            UserClaims =
            {
                SnakeNames.GameId, SnakeNames.Score, SnakeNames.Length, SnakeNames.Level,
                SnakeNames.Pellets, SnakeNames.PathHash, "name", SnakeNames.Nickname,
            },
        },
    ];

    /// <summary>A browser client with no secret: it relies on PKCE and on short-lived tokens.</summary>
    public static IEnumerable<Client> Clients(string gameOrigin) =>
    [
        new Client
        {
            ClientId = SnakeNames.ClientId,
            ClientName = "TokenSnake",

            // Public client: nobody can keep a secret in a browser.
            RequireClientSecret = false,
            RequirePkce = true,
            AllowedGrantTypes = { GrantType.AuthorizationCode, SnakeNames.EatGrantType },

            // No refresh tokens: an expired token ends the game.
            AllowOfflineAccess = false,
            AccessTokenType = AccessTokenType.Jwt,
            AccessTokenLifetime = 300, // the "lobby" token; the game tokens are shorter

            RedirectUris = { $"{gameOrigin}/" },
            PostLogoutRedirectUris = { $"{gameOrigin}/" },
            AllowedCorsOrigins = { gameOrigin },
            RequireConsent = false,

            AllowedScopes = { IdentityServerConstants.StandardScopes.OpenId,
                              IdentityServerConstants.StandardScopes.Profile,
                              SnakeNames.ApiScope },
        },
    ];
}
