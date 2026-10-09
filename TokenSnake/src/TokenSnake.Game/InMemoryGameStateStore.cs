using Microsoft.Extensions.Caching.Memory;

namespace TokenSnake.Game;

/// <summary>
/// Holds games in the shared <see cref="IMemoryCache"/>. A game (active or finished) is dropped after
/// <c>idleExpiry</c> (default 10 minutes) without activity. There is no cap on the number of games.
/// </summary>
public sealed class InMemoryGameStateStore(IMemoryCache cache, TimeSpan? idleExpiry = null) : IGameStateStore
{
    public static readonly TimeSpan DefaultIdleExpiry = TimeSpan.FromMinutes(10);

    // MemoryCache has no atomic compare-and-swap; this lock makes get-compare-set atomic (single-instance demo).
    private readonly object _lock = new();

    public void Create(GameState state)
    {
        lock (_lock) Set(state);
    }

    public GameState? Get(string gameId) => cache.TryGetValue(Key(gameId), out GameState? state) ? state : null;

    public bool TryUpdate(GameState next, long expectedSeq)
    {
        lock (_lock)
        {
            // Over games are final, and a stale Seq means someone else got there first.
            if (!cache.TryGetValue(Key(next.GameId), out GameState? current)
                || current!.Seq != expectedSeq || current.Status == GameStatus.Over) return false;
            Set(next);
            return true;
        }
    }

    private static string Key(string gameId) => "game:" + gameId;

    private void Set(GameState state) =>
        cache.Set(Key(state.GameId), state, new MemoryCacheEntryOptions { SlidingExpiration = idleExpiry ?? DefaultIdleExpiry });
}
