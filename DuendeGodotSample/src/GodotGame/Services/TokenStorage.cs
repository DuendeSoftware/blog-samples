namespace GodotGame.Services;

/// <summary>
/// Static in-memory token store. Tokens are held only for the lifetime of the process —
/// no persistence to disk. Reset by calling Clear() or restarting the game.
/// </summary>
public static class TokenStorage
{
    public static string? AccessToken { get; set; }
    public static string? IdToken { get; set; }
    public static string? RefreshToken { get; set; }

    /// <summary>Decoded claims from the ID token, keyed by claim type.</summary>
    public static Dictionary<string, string> UserClaims { get; set; } = new();

    /// <summary>True when an access token is present.</summary>
    public static bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);

    /// <summary>Clears all stored tokens and claims (logout).</summary>
    public static void Clear()
    {
        AccessToken = null;
        IdToken = null;
        RefreshToken = null;
        UserClaims.Clear();
    }
}
