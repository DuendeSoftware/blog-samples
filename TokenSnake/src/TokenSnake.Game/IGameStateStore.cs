namespace TokenSnake.Game;

public interface IGameStateStore
{
    /// <summary>Adds a new game.</summary>
    void Create(GameState state);

    GameState? Get(string gameId);

    /// <summary>
    /// Replaces the stored state only if its Seq still equals <paramref name="expectedSeq"/>
    /// (optimistic concurrency): of two simultaneous eats, only one wins.
    /// </summary>
    bool TryUpdate(GameState next, long expectedSeq);
}
