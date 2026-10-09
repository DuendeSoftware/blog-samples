using System.Text.RegularExpressions;

namespace TokenSnake.IdentityServer;

/// <summary>The only "user data" TokenSnake knows about: a display nickname.</summary>
public static partial class Nickname
{
    public const int MinLength = 2;
    public const int MaxLength = 20;

    // No ':' on purpose: the sub is "{nickname}:{guid}", so the last ':' always splits it.
    [GeneratedRegex("^[A-Za-z0-9 _.-]+$")]
    private static partial Regex AllowedCharacters();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static bool TryCreate(string? input, out string nickname, out string? error, IEnumerable<string>? blocklist = null)
    {
        // Trim and collapse whitespace first, so "  Ada   Lovelace " becomes "Ada Lovelace".
        nickname = Whitespace().Replace((input ?? "").Trim(), " ");
        error = Validate(nickname, blocklist ?? []);
        return error is null;
    }

    /// <summary>Reads the nickname back out of a <c>sub</c> (split on the last ':').</summary>
    public static string FromSubject(string sub)
    {
        var colon = sub.LastIndexOf(':');
        return colon > 0 ? sub[..colon] : sub;
    }

    private static string? Validate(string nickname, IEnumerable<string> blocklist)
    {
        if (nickname.Length is < MinLength or > MaxLength)
            return $"Pick a nickname between {MinLength} and {MaxLength} characters.";

        if (!AllowedCharacters().IsMatch(nickname))
            return "Use only letters, digits, spaces, dots, dashes and underscores.";

        // Case-insensitive "contains" match against the configured words.
        var blocked = blocklist.Any(word =>
            !string.IsNullOrWhiteSpace(word) && nickname.Contains(word.Trim(), StringComparison.OrdinalIgnoreCase));
        return blocked ? "That nickname isn't available. Try another one." : null;
    }
}

/// <summary>Configured from <c>TokenSnake:NicknameBlocklist</c>.</summary>
public sealed record NicknameBlocklist(IReadOnlyList<string> Words);
