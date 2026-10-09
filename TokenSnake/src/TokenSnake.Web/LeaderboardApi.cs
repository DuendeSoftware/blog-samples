using System.Security.Claims;

namespace TokenSnake.Web;

public sealed record LeaderboardEntry(string Sub, string GameId, string Nickname, int Score, DateTimeOffset At);

/// <summary>In-memory board. It is capped, so a flood of games can't grow it without bound.</summary>
public sealed class LeaderboardStore(int maxEntries = 1000)
{
    private readonly Dictionary<(string Sub, string GameId), LeaderboardEntry> _entries = [];
    private readonly Lock _lock = new();

    /// <summary>Keeps the highest score per (sub, game_id). Returns the score now on the board.</summary>
    public int Submit(LeaderboardEntry entry)
    {
        lock (_lock)
        {
            var key = (entry.Sub, entry.GameId);
            // Scores only go up within a game, so an older token can never lower it.
            if (_entries.TryGetValue(key, out var existing) && existing.Score >= entry.Score)
                return existing.Score;

            _entries[key] = entry;
            if (_entries.Count > maxEntries)
            {
                var lowest = _entries.Values.OrderBy(e => e.Score).ThenByDescending(e => e.At).First();
                _entries.Remove((lowest.Sub, lowest.GameId));
            }
            return _entries.TryGetValue(key, out var kept) ? kept.Score : 0;
        }
    }

    public IReadOnlyList<LeaderboardEntry> Top(int count)
    {
        lock (_lock)
            return _entries.Values.OrderByDescending(e => e.Score).ThenBy(e => e.At).Take(count).ToList();
    }
}

public static class LeaderboardApi
{
    public const string Scheme = "Leaderboard";
    public const string Policy = "Leaderboard";

    public static void Map(IEndpointRouteBuilder app, TimeProvider time)
    {
        app.MapGet("/api/leaderboard", (LeaderboardStore store) =>
            store.Top(10).Select(e => new { nickname = e.Nickname, score = e.Score }));

        app.MapPost("/api/leaderboard", (ClaimsPrincipal user, LeaderboardStore store) =>
            {
                // The request body is ignored on purpose: everything comes from the signed token.
                var gameId = user.FindFirstValue("game_id");
                var sub = user.FindFirstValue("sub");
                if (gameId is null || sub is null || !int.TryParse(user.FindFirstValue("score"), out var score) || score < 0)
                    return Results.BadRequest(new { error = "token_has_no_game" });

                // Stored raw. It is only ever exposed as JSON, and the client renders it with textContent.
                var nickname = user.FindFirstValue("nickname") ?? "anonymous";
                var best = store.Submit(new LeaderboardEntry(sub, gameId, nickname, score, time.GetUtcNow()));
                return Results.Ok(new { score = best });
            })
            .RequireAuthorization(Policy);
    }
}
