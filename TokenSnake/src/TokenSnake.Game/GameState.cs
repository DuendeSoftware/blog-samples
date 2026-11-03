namespace TokenSnake.Game;

public enum GameStatus { Active, Over }

/// <summary>
/// The server-side truth about one game. Immutable: every change produces a new
/// instance (<c>with</c>), which makes optimistic concurrency in the store simple.
/// </summary>
public sealed record GameState
{
    public required string GameId { get; init; }
    public required string Subject { get; init; }

    /// <summary>Secret. It never leaves the server; the browser only sees pellet positions.</summary>
    public required byte[] Seed { get; init; }

    public int Score { get; init; }
    public int Length => Body.Count;
    public int Level => LevelRules.LevelFor(Pellets);
    /// <summary>Pellets eaten so far; also the index of the current pellet.</summary>
    public int Pellets { get; init; }

    /// <summary>The snake, head first.</summary>
    public required IReadOnlyList<Cell> Body { get; init; }
    public Direction Direction { get; init; }

    /// <summary>SHA-256 chain over every move string eaten so far.</summary>
    public string PathHash { get; init; } = PathHasher.Genesis;

    /// <summary>When the previous pellet was eaten (or the game started).</summary>
    public DateTimeOffset LastEatAt { get; init; }

    /// <summary>Number of accepted eats. The next eat must carry Seq + 1.</summary>
    public long Seq { get; init; }

    public GameStatus Status { get; init; } = GameStatus.Active;

    public Cell CurrentPellet(GameRules rules) =>
        PelletGenerator.Get(Seed, Pellets, rules, Body);

    public static GameState Start(string gameId, string subject, byte[] seed, GameRules rules, DateTimeOffset now) => new()
    {
        GameId = gameId,
        Subject = subject,
        Seed = seed,
        Body = rules.CreateStartBody(),
        Direction = rules.StartDirection,
        LastEatAt = now,
    };
}
